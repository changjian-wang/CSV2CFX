using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Extensions
{
    public static class MessageEncoder
    {
        /// <summary>
        /// 将 JSON 字符串进行 GZip 压缩并 Base64 编码
        /// </summary>
        public static string EncodeMessage(string jsonMessage)
        {
            if (string.IsNullOrEmpty(jsonMessage))
            {
                return string.Empty;
            }

            try
            {
                // 1. 转换为字节
                byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonMessage);

                // 2. GZip 压缩
                using (var outputStream = new MemoryStream())
                {
                    using (var gzipStream = new GZipStream(outputStream, CompressionMode.Compress))
                    {
                        gzipStream.Write(jsonBytes, 0, jsonBytes.Length);
                    }

                    // 3. Base64 编码
                    byte[] compressedBytes = outputStream.ToArray();
                    return Convert.ToBase64String(compressedBytes);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"消息编码失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 解码消息（用于调试）
        /// </summary>
        public static string DecodeMessage(string encodedMessage)
        {
            if (string.IsNullOrEmpty(encodedMessage))
            {
                return string.Empty;
            }

            try
            {
                byte[] compressedBytes = Convert.FromBase64String(encodedMessage);

                using (var inputStream = new MemoryStream(compressedBytes))
                using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
                using (var outputStream = new MemoryStream())
                {
                    gzipStream.CopyTo(outputStream);
                    byte[] decompressedBytes = outputStream.ToArray();
                    return Encoding.UTF8.GetString(decompressedBytes);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"消息解码失败: {ex.Message}", ex);
            }
        }
    }
}
