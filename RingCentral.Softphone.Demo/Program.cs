using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using RingCentral;
using dotenv.net;

namespace RingCentral.Softphone.Demo
{
    class Program
    {
        static void Main(string[] args)
        {
            DotEnv.Config(true);

            Task.Run(async () =>
            {
                var rc = new RestClient(
                    Environment.GetEnvironmentVariable("RINGCENTRAL_CLIENT_ID"),
                    Environment.GetEnvironmentVariable("RINGCENTRAL_CLIENT_SECRET"),
                    Environment.GetEnvironmentVariable("RINGCENTRAL_SERVER_URL")
                );
                await rc.Authorize(
                    Environment.GetEnvironmentVariable("RINGCENTRAL_USERNAME"),
                    Environment.GetEnvironmentVariable("RINGCENTRAL_EXTENSION"),
                    Environment.GetEnvironmentVariable("RINGCENTRAL_PASSWORD")
                );
                Console.WriteLine(rc.token.access_token);
                await rc.Revoke();
            }).GetAwaiter().GetResult();
            // using var client = new TcpClient();
            //
            // var hostname = "webcode.me";
            // client.Connect(hostname, 80);
            //
            // using NetworkStream networkStream = client.GetStream();
            // networkStream.ReadTimeout = 2000;
            //
            // using var writer = new StreamWriter(networkStream);
            //
            // var message = "HEAD / HTTP/1.1\r\nHost: webcode.me\r\nUser-Agent: C# program\r\n" 
            //               + "Connection: close\r\nAccept: text/html\r\n\r\n";
            //
            // Console.WriteLine(message);
            //
            // using var reader = new StreamReader(networkStream, Encoding.UTF8);
            //
            // byte[] bytes = Encoding.UTF8.GetBytes(message);
            // networkStream.Write(bytes, 0, bytes.Length);
            //
            // Console.WriteLine(reader.ReadToEnd());
        }
    }
}