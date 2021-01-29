using System;
using System.Collections.Generic;
using System.Linq;

namespace RingCentral.Softphone.Net
{
    public class SipMessage
    {
        public string Subject { get; set; }
        public Dictionary<string, string> Headers { get; set; }
        public string Body { get; set; }

        public SipMessage(string subject, Dictionary<string, string> headers, string body)
        {
            this.Subject = subject;
            this.Headers = headers;
            this.Body = body;
        }

        public static SipMessage FromMessage(string message)
        {
            var tokens = message.Split(new []{"\r\n\r\n"}, StringSplitOptions.None);
            var init = tokens[0];
            var tail = tokens.Skip(1);
            var body = string.Join("\r\n\r\n", tail);
            tokens = init.Split(new[] {"\r\n"}, StringSplitOptions.None);
            var subject = tokens[0];
            var lines = tokens.Skip(1);
            var headers = lines.Select(line => line.Split(new[] {": "}, StringSplitOptions.None))
                .ToDictionary(ts => ts[0], ts => ts[1]);
            return new SipMessage(subject, headers, body);
        }

        public string ToMessage()
        {
            var list = new List<string>();
            list.Add(this.Subject);
            foreach (var item in this.Headers)
            {
                list.Add($"{item.Key}: {item.Value}");
            }
            list.Add("");
            list.Add(this.Body);
            return string.Join("\r\n", list);
        }
    }
}