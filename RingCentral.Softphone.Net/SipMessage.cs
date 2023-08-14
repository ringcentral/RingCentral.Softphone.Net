using System;
using System.Collections.Generic;
using System.Linq;

namespace RingCentral.Softphone.Net
{
    public class SipMessage
    {
        public SipMessage(string subject, Dictionary<string, string> headers, string body)
        {
            Subject = subject;
            Headers = headers;
            Body = body;
        }

        public string Subject { get; set; }
        public Dictionary<string, string> Headers { get; set; }
        public string Body { get; set; }

        public static SipMessage FromMessage(string message)
        {
            var tokens = message.Split(new[] {"\r\n\r\n"}, StringSplitOptions.None);
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
            list.Add(Subject);
            foreach (var item in Headers) list.Add($"{item.Key}: {item.Value}");
            list.Add("");
            list.Add(Body);
            return string.Join("\r\n", list);
        }
    }
}