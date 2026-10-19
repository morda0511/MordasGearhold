using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace MordasGearhold.Core
{
    /// <summary>
    /// A stored equipment set: a list of raw item records plus a header. Encoded as
    /// base64 of [magic, format, payload, sha256(payload)] so truncation or bit rot is detected.
    /// The hash only DETECTS damage; damaged packages are quarantined, never repaired or discarded.
    /// </summary>
    internal sealed class SetPackage
    {
        private const string Magic = "MGH";
        private const string PayloadMagic = "MGH-SET";
        internal const int Format = 1;

        public int SetIndex;
        public List<byte[]> Records = new List<byte[]>();

        public bool IsEmpty => Records.Count == 0;

        public string Encode()
        {
            ZPackage payload = new ZPackage();
            payload.Write(PayloadMagic);
            payload.Write(Format);
            payload.Write(SetIndex);
            payload.Write(Records.Count);
            foreach (byte[] record in Records)
            {
                payload.Write(record);
            }
            byte[] bytes = payload.GetArray();
            ZPackage outer = new ZPackage();
            outer.Write(Magic);
            outer.Write(Format);
            outer.Write(bytes);
            outer.Write(Hash(bytes));
            return outer.GetBase64();
        }

        public static bool TryDecode(string encoded, out SetPackage package, out string error)
        {
            package = null;
            error = null;
            try
            {
                ZPackage outer = new ZPackage(encoded);
                if (outer.ReadString() != Magic)
                {
                    error = "unknown package header";
                    return false;
                }
                int format = outer.ReadInt();
                if (format > Format)
                {
                    error = "package format " + format + " is newer than this mod version";
                    return false;
                }
                byte[] bytes = outer.ReadByteArray();
                byte[] hash = outer.ReadByteArray();
                if (!SameBytes(hash, Hash(bytes)))
                {
                    error = "checksum mismatch";
                    return false;
                }
                ZPackage payload = new ZPackage(bytes);
                if (payload.ReadString() != PayloadMagic)
                {
                    error = "unknown payload header";
                    return false;
                }
                payload.ReadInt();
                SetPackage result = new SetPackage { SetIndex = payload.ReadInt() };
                int count = payload.ReadInt();
                if (count < 0 || count > 4096)
                {
                    error = "implausible record count " + count;
                    return false;
                }
                for (int i = 0; i < count; i++)
                {
                    result.Records.Add(payload.ReadByteArray());
                }
                package = result;
                return true;
            }
            catch (Exception ex)
            {
                error = "unreadable package: " + ex.Message;
                return false;
            }
        }

        private static byte[] Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return sha.ComputeHash(bytes);
            }
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
