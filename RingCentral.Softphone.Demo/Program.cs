using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using dotenv.net;
using RingCentral.Softphone.Net;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using Nager.TcpClient;

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
                    Environment.GetEnvironmentVariable("RINGCENTRAL_JWT_TOKEN")
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

                var client = new TcpClient();
                var tokens = sipInfo.outboundProxy.Split(":");
                await client.ConnectAsync(tokens[0], int.Parse(tokens[1]));
                var rtpSession = new RTPSession(false, false, false);

                var userAgent = "RingCentral.Softphone.Net";
                var fakeDomain = $"{Guid.NewGuid().ToString()}.invalid";
                var fakeEmail = $"{Guid.NewGuid().ToString()}@{fakeDomain}";

                var registrationMessage = new SipMessage($"REGISTER sip:{sipInfo.domain} SIP/2.0",
                    new Dictionary<string, string>
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

                var cachedMessages = "";

                // receive message
                async void OnDataReceived(byte[] receivedData)
                {
                    var data = Encoding.UTF8.GetString(receivedData);
                    Console.WriteLine("Receiving...\n" + data);
                    cachedMessages += data;
                }

                client.DataReceived += OnDataReceived;

                // send message
                async void SendMessage(SipMessage sipMessage)
                {
                    var message = sipMessage.ToMessage();
                    Console.WriteLine("Sending...\n" + message);
                    var bytes = Encoding.UTF8.GetBytes(message);
                    await client.SendAsync(bytes);
                }

                // send first registration message
                SendMessage(registrationMessage);

                // Do not exit, wait for the incoming audio
                var noMoreMessage = false;
                while (true) // wait for server message forever
                {
                    await Task.Delay(100);
                    if (cachedMessages.Length > 0)
                    {
                        if (noMoreMessage)
                        {
                            var tempTokens = cachedMessages.Split("\r\n\r\nSIP/2.0 ");
                            // sometimes we receive two messages in one data
                            if (tempTokens.Length > 1)
                            {
                                // in this case, we only need the second one
                                cachedMessages = "SIP/2.0 " + tempTokens[1];
                            }

                            var sipMessage = SipMessage.FromMessage(cachedMessages);
                            cachedMessages = "";

                            // the message after we reply to INVITE
                            if (sipMessage.Subject.StartsWith("ACK sip:"))
                            {
                                // The purpose of sending a DTMF tone is if our SDP had a private IP address then the server needs to get at least
                                // one RTP packet to know where to send.
                                await rtpSession.SendDtmf(0, CancellationToken.None);
                            }

                            // authorize failed with nonce in header
                            if (sipMessage.Subject.StartsWith("SIP/2.0 401 Unauthorized"))
                            {
                                var nonceMessage = sipMessage;
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
                                registrationMessage.Headers["Authorization"] = auth;
                                registrationMessage.Headers["CSeq"] = "8083 REGISTER";
                                registrationMessage.Headers["Via"] =
                                    $"SIP/2.0/TCP {fakeDomain};branch=z9hG4bK{Guid.NewGuid().ToString()}";
                                SendMessage(registrationMessage);
                            }

                            // whenever there is an inbound call
                            if (sipMessage.Subject.StartsWith("INVITE sip:"))
                            {
                                var inviteSipMessage = sipMessage;
                                MediaStreamTrack audioTrack = new MediaStreamTrack(new List<AudioFormat>
                                    {new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU)});
                                rtpSession.addTrack(audioTrack);
                                var result =
                                    rtpSession.SetRemoteDescription(SdpType.offer,
                                        SDP.ParseSDPDescription(inviteSipMessage.Body));
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
                                SendMessage(sipMessage);
                            }
                        }
                        else
                        {
                            noMoreMessage = true;
                        }
                    }
                }
            }).GetAwaiter().GetResult();
        }
    }
}