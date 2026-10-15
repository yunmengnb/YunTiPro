using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace YunTiPro
{
    /// <summary>
    /// 节点存储：整份 JSON 经 DPAPI(CurrentUser) 加密后落盘。
    /// 密文仅当前 Windows 用户可解密。
    /// </summary>
    public class CredentialStore
    {
        private readonly string _filePath;
        private readonly object _lock = new object();
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("YunTiPro.Win.v1");

        public CredentialStore(string filePath)
        {
            _filePath = filePath;
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
        }

        public List<NodeConfig> Load()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(_filePath)) return new List<NodeConfig>();
                    byte[] cipher = File.ReadAllBytes(_filePath);
                    if (cipher.Length == 0) return new List<NodeConfig>();
                    byte[] plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
                    string json = Encoding.UTF8.GetString(plain);
                    var ser = new JavaScriptSerializer();
                    ser.MaxJsonLength = 10 * 1024 * 1024;
                    var list = ser.Deserialize<List<NodeConfig>>(json);
                    return list ?? new List<NodeConfig>();
                }
                catch (Exception)
                {
                    // 解密失败（换用户/文件损坏）时返回空列表，避免崩溃
                    return new List<NodeConfig>();
                }
            }
        }

        public void Save(List<NodeConfig> nodes)
        {
            lock (_lock)
            {
                var ser = new JavaScriptSerializer();
                ser.MaxJsonLength = 10 * 1024 * 1024;
                string json = ser.Serialize(nodes ?? new List<NodeConfig>());
                byte[] plain = Encoding.UTF8.GetBytes(json);
                byte[] cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                // 先写临时文件再替换，避免写入中断导致文件损坏
                string tmp = _filePath + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(cipher, 0, cipher.Length);
                }
                if (File.Exists(_filePath)) File.Delete(_filePath);
                File.Move(tmp, _filePath);
            }
        }

        /// <summary>加密往返测试辅助：加密再解密应还原原文</summary>
        public static string EncryptRoundTrip(string plainText)
        {
            byte[] cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), Entropy, DataProtectionScope.CurrentUser);
            byte[] plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
    }
}
