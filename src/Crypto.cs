using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LocalCodeRunner
{
    // The same scheme as lcr.py; pack.py checks the two agree before packing.
    static class Crypto
    {
        public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        public static string NormalizeCode(string text)
        {
            var sb = new StringBuilder();
            foreach (char raw in (text ?? "").ToUpperInvariant())
            {
                char ch = raw == 'O' ? '0' : raw == 'I' || raw == 'L' ? '1' : raw;
                if (Alphabet.IndexOf(ch) >= 0) sb.Append(ch);
            }
            return sb.ToString();
        }

        public static byte[] Derive(string code, byte[] salt, int iterations, int length)
        {
            var password = Encoding.ASCII.GetBytes(NormalizeCode(code));
            using (var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
                return kdf.GetBytes(length);
        }

        public static byte[] Hmac(byte[] key, params byte[][] parts)
        {
            using (var h = new HMACSHA256(key))
            {
                foreach (var p in parts) h.TransformBlock(p, 0, p.Length, null, 0);
                h.TransformFinalBlock(new byte[0], 0, 0);
                return h.Hash;
            }
        }

        // HMAC-SHA256 in counter mode: block i of the stream is HMAC(key, nonce || i).
        public static byte[] KeystreamXor(byte[] key, byte[] nonce, byte[] data)
        {
            var output = new byte[data.Length];
            var counter = new byte[8];
            using (var h = new HMACSHA256(key))
            {
                for (long block = 0; block * 32 < data.Length; block++)
                {
                    for (int k = 0; k < 8; k++) counter[k] = (byte)(block >> (56 - 8 * k));
                    h.Initialize();
                    h.TransformBlock(nonce, 0, nonce.Length, null, 0);
                    h.TransformFinalBlock(counter, 0, 8);
                    var stream = h.Hash;
                    long start = block * 32;
                    for (int j = 0; j < 32 && start + j < data.Length; j++)
                        output[start + j] = (byte)(data[start + j] ^ stream[j]);
                }
            }
            return output;
        }

        public static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public static string Receipt(byte[] data)
        {
            byte[] digest;
            using (var sha = SHA256.Create()) digest = sha.ComputeHash(data);
            ulong value = 0;
            for (int i = 0; i < 8; i++) value = (value << 8) | digest[i];
            value >>= 4;
            var chars = new char[12];
            for (int i = 0; i < 12; i++) chars[i] = Alphabet[(int)((value >> (5 * (11 - i))) & 31)];
            var s = new string(chars);
            return s.Substring(0, 4) + "-" + s.Substring(4, 4) + "-" + s.Substring(8, 4);
        }

        public static string Hex(byte[] data) =>
            string.Concat(data.Select(b => b.ToString("x2")));

        public static byte[] Gunzip(byte[] data)
        {
            using (var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                input.CopyTo(output);
                return output.ToArray();
            }
        }
    }
}
