using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using dotenv.net;
using RingCentral.Softphone.Net;

namespace RingCentral.Softphone.Demo
{
    class Program
    {
        static void Main(string[] args)
        {
            DotEnv.Config(true);

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
                    {"Max-Forwards", "70"},
                }, "");
                
                // write
                var message = sipMessage.ToMessage();
                Console.WriteLine(message);
                var bytes = Encoding.UTF8.GetBytes(message);
                await networkStream.WriteAsync(bytes, 0, bytes.Length);
                
                // read
                var cache = new byte[1024];
                var bytesRead = await networkStream.ReadAsync(cache, 0, cache.Length);
                Console.WriteLine(Encoding.UTF8.GetString(cache,0,bytesRead));
                bytesRead = await networkStream.ReadAsync(cache, 0, cache.Length);
                Console.WriteLine(Encoding.UTF8.GetString(cache,0,bytesRead));
            }).GetAwaiter().GetResult();
        }
    }
}