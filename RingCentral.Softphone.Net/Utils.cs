using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RingCentral.Softphone.Net
{
    public static class Utils
    {
        public static string Md5(string text)
        {
            using (var hash = MD5.Create())
            {
                return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(x => x.ToString("x2")));
            }
        }

        public static string GenerateResponse(string username, string password, string realm, string method, string uri,
            string nonce)
        {
            var ha1 = Md5($"{username}:{realm}:{password}");
            var ha2 = Md5($"{method}:{uri}");
            return Md5($"{ha1}:{nonce}:{ha2}");
        }

        public static string GenerateAuthorization(SipInfoResponse sipInfo, string method, string nonce)
        {
            var username = sipInfo.authorizationId;
            var password = sipInfo.password;
            var realm = sipInfo.domain;
            return
                $"Digest algorithm=MD5, username=\"{username}\", realm=\"{realm}\", nonce=\"{nonce}\", uri=\"sip:{realm}\", response=\"{GenerateResponse(username, password, realm, method, $"sip:{realm}", nonce)}\"";
        }
    }
}