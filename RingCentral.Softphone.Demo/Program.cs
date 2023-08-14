using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using dotenv.net;
using RingCentral.Softphone.Net;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

namespace RingCentral.Softphone.Demo
{
    class Program
    {
        static void Main(string[] args)
        {
            DotEnv.Load(new DotEnvOptions().WithOverwriteExistingVars());

            Task.Run(async () =>
            {
                using var rc = new RestClient(
                    Environment.GetEnvironmentVariable("RINGCENTRAL_CLIENT_ID"),
                    Environment.GetEnvironmentVariable("RINGCENTRAL_CLIENT_SECRET"),
                    Environment.GetEnvironmentVariable("RINGCENTRAL_SERVER_URL")
                );
                await rc.Authorize(
                    Environment.GetEnvironmentVariable("RINGCENTRAL_USERNAME"),
                    Environment.GetEnvironmentVariable("RINGCENTRAL_EXTENSION"),
                    Environment.GetEnvironmentVariable("RINGCENTRAL_PASSWORD")
                );
                var sipProvision = await rc.Restapi().ClientInfo().SipProvision().Post(new CreateSipRegistrationRequest
                {
                    sipInfo = new[]
                    {
                        new SIPInfoRequest
                        {
                            transport = "TCP"
                        }
                    },
                    device = new DeviceInfoRequest
                    {
                        computerName = Environment.MachineName
                    }
                });
                var sipInfo = sipProvision.sipInfo[0];

                using var client = new TcpClient();
                var tokens = sipInfo.outboundProxy.Split(":");
                await client.ConnectAsync(tokens[0], int.Parse(tokens[1]));

                await using var networkStream = client.GetStream();
                var userAgent = "RingCentral.Softphone.Net";
                var fakeDomain = $"{Guid.NewGuid().ToString()}.invalid";
                var fakeEmail = $"{Guid.NewGuid().ToString()}@{fakeDomain}";
                var sipMessage = new SipMessage($"REGISTER sip:{sipInfo.domain} SIP/2.0", new Dictionary<string, string>
                {
                    {"Call-ID", Guid.NewGuid().ToString()},
                    {"User-Agent", userAgent},
                    {"Contact", $"<sip:{fakeEmail};transport=tcp>;expires=600"},
                    {"Via", $"SIP/2.0/TCP {fakeDomain};branch=z9hG4bK{Guid.NewGuid().ToString()}"},
                    {"From", $"<sip:{sipInfo.username}@{sipInfo.domain}>;tag={Guid.NewGuid().ToString()}"},
                    {"To", $"<sip:{sipInfo.username}@{sipInfo.domain}>"},
                    {"CSeq", "8082 REGISTER"},
                    {"Content-Length", "0"},
                    {"Max-Forwards", "70"}
                }, "");

                // write
                var message = sipMessage.ToMessage();
                Console.WriteLine(message);
                var bytes = Encoding.UTF8.GetBytes(message);
                await networkStream.WriteAsync(bytes, 0, bytes.Length);

                // read
                var cache = new byte[10240];

                // 100 trying
                var bytesRead = await networkStream.ReadAsync(cache, 0, cache.Length);
                Console.WriteLine(Encoding.UTF8.GetString(cache, 0, bytesRead));

                // 401 Unauthorized
                bytesRead = await networkStream.ReadAsync(cache, 0, cache.Length);
                Console.WriteLine(Encoding.UTF8.GetString(cache, 0, bytesRead));
                var nonceMessage = SipMessage.FromMessage(Encoding.UTF8.GetString(cache, 0, bytesRead));
                var wwwAuth = "";
                if (nonceMessage.Headers.ContainsKey("WWW-Authenticate"))
                {
                    wwwAuth = nonceMessage.Headers["WWW-Authenticate"];
                }
                else if (nonceMessage.Headers.ContainsKey("Www-Authenticate"))
                {
                    wwwAuth = nonceMessage.Headers["Www-Authenticate"];
                }

                var regex = new Regex(", nonce=\"(.+?)\"");
                var match = regex.Match(wwwAuth);
                var nonce = match.Groups[1].Value;
                var auth = Net.Utils.GenerateAuthorization(sipInfo, "REGISTER", nonce);
                sipMessage.Headers["Authorization"] = auth;
                sipMessage.Headers["CSeq"] = "8083 REGISTER";
                sipMessage.Headers["Via"] = $"SIP/2.0/TCP {fakeDomain};branch=z9hG4bK{Guid.NewGuid().ToString()}";

                // write
                message = sipMessage.ToMessage();
                Console.WriteLine(message);
                bytes = Encoding.UTF8.GetBytes(message);
                await networkStream.WriteAsync(bytes, 0, bytes.Length);

                // 100 trying
                bytesRead = await networkStream.ReadAsync(cache, 0, cache.Length);
                Console.WriteLine(Encoding.UTF8.GetString(cache, 0, bytesRead));

                // 200 OK
                bytesRead = await networkStream.ReadAsync(cache, 0, cache.Length);
                Console.WriteLine(Encoding.UTF8.GetString(cache, 0, bytesRead));

                // Inbound INVITE
                bytesRead = await networkStream.ReadAsync(cache, 0, cache.Length);
                Console.WriteLine(Encoding.UTF8.GetString(cache, 0, bytesRead));
                var inviteMessage = Encoding.UTF8.GetString(cache, 0, bytesRead);
                var inviteSipMessage = SipMessage.FromMessage(inviteMessage);

                // RTP
                RTPSession rtpSession = new RTPSession(false, false, false);
                MediaStreamTrack audioTrack = new MediaStreamTrack(new List<AudioFormat>
                    {new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU)});
                rtpSession.addTrack(audioTrack);
                var result =
                    rtpSession.SetRemoteDescription(SdpType.offer, SDP.ParseSDPDescription(inviteSipMessage.Body));
                Console.WriteLine(result);
                var answer = rtpSession.CreateAnswer(null);

                rtpSession.OnRtpPacketReceived +=
                    (IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket) =>
                    {
                        Console.WriteLine("OnRtpPacketReceived");
                    };

                sipMessage =
                    new SipMessage("SIP/2.0 200 OK", new Dictionary<string, string>
                    {
                        {"Contact", $"<sip:{fakeEmail};transport=tcp>"},
                        {"Content-Type", "application/sdp"},
                        {"Content-Length", answer.ToString().Length.ToString()},
                        {"User-Agent", "RingCentral.Softphone.Net"},
                        {"Via", inviteSipMessage.Headers["Via"]},
                        {"From", inviteSipMessage.Headers["From"]},
                        {"To", $"{inviteSipMessage.Headers["To"]};tag={Guid.NewGuid().ToString()}"},
                        {"CSeq", inviteSipMessage.Headers["CSeq"]},
                        {"Supported", "outbound"},
                        {"Call-Id", inviteSipMessage.Headers["Call-Id"]}
                    }, answer.ToString());

                // write
                message = sipMessage.ToMessage();
                Console.WriteLine(message);
                bytes = Encoding.UTF8.GetBytes(message);
                await networkStream.WriteAsync(bytes, 0, bytes.Length);

                // ACK
                bytesRead = await networkStream.ReadAsync(cache, 0, cache.Length);
                Console.WriteLine(Encoding.UTF8.GetString(cache, 0, bytesRead));

                // The purpose of sending a DTMF tone is if our SDP had a private IP address then the server needs to get at least
                // one RTP packet to know where to send.
                await rtpSession.SendDtmf(0, CancellationToken.None);

                // Do not exit, wait for the incoming audio
                await Task.Delay(999999999);
            }).GetAwaiter().GetResult();
        }
    }
}