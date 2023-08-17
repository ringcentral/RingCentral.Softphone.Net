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
using Websocket.Client;
using System.Net.WebSockets;

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
                            transport = "WSS"
                        }
                    }
                });
                var sipInfo = sipProvision.sipInfo[0];
                var wsUri = "wss://" + sipInfo.outboundProxy;
                var factory = new Func<ClientWebSocket>(() =>
                {
                    var cws = new ClientWebSocket();
                    cws.Options.AddSubProtocol("sip");
                    return cws;
                });
                var ws = new WebsocketClient(new Uri(wsUri), factory);
                ws.ReconnectTimeout = null;

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

                ws.MessageReceived.Subscribe(responseMessage =>
                {
                    Console.WriteLine("Receiving...\n" + responseMessage.Text);
                    var sipMessage = SipMessage.FromMessage(responseMessage.Text);

                    // authorize failed with nonce in header
                    if (sipMessage.Subject == "SIP/2.0 401 Unauthorized")
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
                        var audioTrack = new MediaStreamTrack(new List<AudioFormat>
                            {new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU)});

                        var rtcPeer = new RTCPeerConnection(new RTCConfiguration
                        {
                            iceServers = new List<RTCIceServer> { new RTCIceServer { urls = "stun:74.125.194.127:19302" } }
                        });
                        rtcPeer.addTrack(audioTrack);
                        rtcPeer.OnRtpPacketReceived += (IPEndPoint arg1, SDPMediaTypesEnum arg2, RTPPacket arg3) =>
                        {
                            Console.WriteLine("OnRtpPacketReceived");
                        };
                        rtcPeer.setRemoteDescription(new RTCSessionDescriptionInit
                        {
                            sdp = inviteSipMessage.Body,
                            type = RTCSdpType.offer
                        });
                        var answer = rtcPeer.createAnswer();
                        rtcPeer.setLocalDescription(answer);
                        
                        // rtpSession.addTrack(audioTrack);
                        // var result =
                        //     rtpSession.SetRemoteDescription(SdpType.offer,
                        //         SDP.ParseSDPDescription(inviteSipMessage.Body));
                        // Console.WriteLine(result);
                        // var answer = rtpSession.CreateAnswer(null);
                        //
                        // rtpSession.OnRtpPacketReceived +=
                        //     (IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket) =>
                        //     {
                        //         Console.WriteLine("OnRtpPacketReceived");
                        //     };

                        sipMessage =
                            new SipMessage("SIP/2.0 200 OK", new Dictionary<string, string>
                            {
                                {"Contact", $"<sip:{fakeEmail};transport=tcp>"},
                                {"Content-Type", "application/sdp"},
                                {"Content-Length", answer.sdp.Length.ToString()},
                                {"User-Agent", "RingCentral.Softphone.Net"},
                                {"Via", inviteSipMessage.Headers["Via"]},
                                {"From", inviteSipMessage.Headers["From"]},
                                {"To", $"{inviteSipMessage.Headers["To"]};tag={Guid.NewGuid().ToString()}"},
                                {"CSeq", inviteSipMessage.Headers["CSeq"]},
                                {"Supported", "outbound"},
                                {"Call-Id", inviteSipMessage.Headers["Call-Id"]}
                            }, answer.sdp);
                        SendMessage(sipMessage);
                    }
                });
                await ws.Start();

                void SendMessage(SipMessage sipMessage)
                {
                    var message = sipMessage.ToMessage();
                    Console.WriteLine("Sending...\n" + message);
                    ws.Send(message);
                }


                SendMessage(registrationMessage);

                await Task.Delay(999999999);
            }).GetAwaiter().GetResult();
        }
    }
}
