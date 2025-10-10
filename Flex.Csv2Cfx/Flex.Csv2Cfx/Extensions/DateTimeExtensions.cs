using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Extensions
{
    /// <summary>
    /// DateTime扩展方法 - 适用于changjian-wang的Azure AI和.NET项目
    /// 时间: 2025-09-08 12:12:57 UTC
    /// </summary>
    public static class DateTimeExtensions
    {
        /// <summary>
        /// 计算两个时间之间的差值
        /// </summary>
        /// <param name="startTime">开始时间 (格式: HH:mm)</param>
        /// <param name="endTime">结束时间 (格式: HH:mm)</param>
        /// <returns>时间差 (格式: HH:mm:ss)</returns>
        public static string CalculateTimeDifference(string startTime, string endTime)
        {
            try
            {
                DateTime start, end;

                // 尝试多种可能的日期时间格式
                string[] formats = {
                    "HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss",
                    "yyyy/MM/dd HH:mm", "yyyy/M/dd HH:mm", "yyyy/MM/d H:mm", "yyyy/M/d H:mm",
                    "yyyy-MM-dd HH:mm", "yyyy-M-dd HH:mm", "yyyy-MM-d HH:mm", "yyyy-M-d HH:mm",
                    "MM/dd/yyyy HH:mm", "M/dd/yyyy HH:mm", "MM/d/yyyy HH:mm", "M/d/yyyy HH:mm"
                };

                // 解析开始时间
                if (!DateTime.TryParseExact(startTime, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
                {
                    throw new ArgumentException($"无法解析开始时间: {startTime}");
                }

                // 解析结束时间
                if (!DateTime.TryParseExact(endTime, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out end))
                {
                    throw new ArgumentException($"无法解析结束时间: {endTime}");
                }

                // 如果结束时间小于开始时间，说明跨越了午夜
                if (end < start)
                {
                    end = end.AddDays(1);
                }

                // 计算时间差
                TimeSpan difference = end - start;

                // 返回格式化的时间差
                return $"{(int)difference.TotalHours:00}:{difference.Minutes:00}:{difference.Seconds:00}";
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"时间格式错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 转换为ISO 8601格式字符串，如: 2024-11-04T12:49:09.176499-06:00
        /// 适用于AI内容理解项目的高精度时间戳需求
        /// </summary>
        public static string ToISO8601String(this DateTime dateTime, TimeZoneInfo? timeZone = null)
        {
            var targetTimeZone = timeZone ?? TimeZoneInfo.Local;

            DateTimeOffset dateTimeOffset = dateTime.Kind switch
            {
                DateTimeKind.Utc => new DateTimeOffset(
                    TimeZoneInfo.ConvertTimeFromUtc(dateTime, targetTimeZone),
                    targetTimeZone.GetUtcOffset(dateTime)),
                DateTimeKind.Local => new DateTimeOffset(dateTime),
                _ => new DateTimeOffset(dateTime, targetTimeZone.GetUtcOffset(dateTime))
            };

            return dateTimeOffset.ToString("yyyy-MM-ddTHH:mm:ss.ffffffK");
        }

        /// <summary>
        /// DateTimeOffset转换为ISO 8601格式字符串
        /// </summary>
        public static string ToISO8601String(this DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.ToString("yyyy-MM-ddTHH:mm:ss.ffffffK");
        }

        /// <summary>
        /// 添加微秒精度 - 适用于Azure AI处理时间的精确记录
        /// </summary>
        public static DateTime AddMicroseconds(this DateTime dateTime, long microseconds)
        {
            return dateTime.AddTicks(microseconds * 10); // 1微秒 = 10 ticks
        }

        /// <summary>
        /// DateTimeOffset添加微秒
        /// </summary>
        public static DateTimeOffset AddMicroseconds(this DateTimeOffset dateTimeOffset, long microseconds)
        {
            return dateTimeOffset.AddTicks(microseconds * 10);
        }

        /// <summary>
        /// 创建精确到微秒的时间戳，格式如: 2024-11-04T12:49:09.176499-06:00
        /// changjian-wang项目专用方法
        /// </summary>
        public static string CreatePreciseTimestamp(
            int year = 2024, int month = 11, int day = 4,
            int hour = 12, int minute = 49, int second = 9,
            int millisecond = 176, int microsecond = 499,
            int timezoneOffsetHours = -6)
        {
            var dateTime = new DateTime(year, month, day, hour, minute, second, millisecond);
            dateTime = dateTime.AddMicroseconds(microsecond);

            var offset = TimeSpan.FromHours(timezoneOffsetHours);
            var dateTimeOffset = new DateTimeOffset(dateTime, offset);

            return dateTimeOffset.ToString("yyyy-MM-ddTHH:mm:ss.ffffffK");
        }

        /// <summary>
        /// 获取当前UTC时间的ISO 8601格式 - 适用于分布式系统和云应用
        /// </summary>
        public static string GetCurrentUTCISO8601()
        {
            return DateTime.UtcNow.ToISO8601String(TimeZoneInfo.Utc);
        }

        /// <summary>
        /// 获取当前本地时间的ISO 8601格式
        /// </summary>
        public static string GetCurrentLocalISO8601()
        {
            return DateTime.Now.ToISO8601String();
        }

        /// <summary>
        /// 转换为Azure服务友好的时间格式
        /// </summary>
        public static string ToAzureTimestamp(this DateTime dateTime)
        {
            var utcDateTime = dateTime.Kind == DateTimeKind.Local
                ? dateTime.ToUniversalTime()
                : dateTime;

            return new DateTimeOffset(utcDateTime, TimeSpan.Zero)
                .ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");
        }

        /// <summary>
        /// 解析ISO 8601格式的时间字符串
        /// </summary>
        public static DateTimeOffset ParseISO8601(this string timestamp)
        {
            if (string.IsNullOrEmpty(timestamp))
                throw new ArgumentException("时间戳不能为空", nameof(timestamp));

            return DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        /// <summary>
        /// 安全解析ISO 8601格式，失败时返回默认值
        /// </summary>
        public static DateTimeOffset? TryParseISO8601(this string timestamp)
        {
            if (string.IsNullOrEmpty(timestamp))
                return null;

            if (DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var result))
            {
                return result;
            }

            return null;
        }

        /// <summary>
        /// 计算两个ISO 8601时间戳之间的时间差
        /// 适用于AI处理性能分析
        /// </summary>
        public static TimeSpan CalculateDuration(string startTimestamp, string endTimestamp)
        {
            var start = startTimestamp.ParseISO8601();
            var end = endTimestamp.ParseISO8601();
            return end - start;
        }

        /// <summary>
        /// 为日志文件生成时间戳文件名
        /// </summary>
        public static string ToLogFileName(this DateTime dateTime, string prefix = "", string extension = ".log")
        {
            var timestamp = dateTime.ToString("yyyyMMdd_HHmmss");
            var fileName = string.IsNullOrEmpty(prefix)
                ? $"{timestamp}{extension}"
                : $"{prefix}_{timestamp}{extension}";

            return fileName;
        }

        /// <summary>
        /// 获取Unix时间戳（毫秒）- 适用于API交互
        /// </summary>
        public static long ToUnixTimestampMilliseconds(this DateTime dateTime)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var utcDateTime = dateTime.Kind == DateTimeKind.Local
                ? dateTime.ToUniversalTime()
                : dateTime;

            return (long)(utcDateTime - epoch).TotalMilliseconds;
        }

        /// <summary>
        /// 从Unix时间戳（毫秒）转换为DateTime
        /// </summary>
        public static DateTime FromUnixTimestampMilliseconds(long unixTimestamp)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddMilliseconds(unixTimestamp);
        }

        /// <summary>
        /// 检查是否为工作日（周一到周五）
        /// </summary>
        public static bool IsWorkingDay(this DateTime dateTime)
        {
            return dateTime.DayOfWeek != DayOfWeek.Saturday && dateTime.DayOfWeek != DayOfWeek.Sunday;
        }

        /// <summary>
        /// 获取指定时区的当前时间
        /// </summary>
        public static DateTime GetTimeInTimeZone(string timeZoneId)
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
        }

        /// <summary>
        /// 将 DateTime 格式化为 ISO 8601 字符串
        /// </summary>
        /// <param name="dateTime">要格式化的 DateTime</param>
        /// <param name="offsetHours">时区偏移小时数</param>
        /// <returns>格式化后的字符串</returns>
        public static string FormatDateTimeToIso8601(this DateTime dateTime, int offsetHours = 8)
        {
            TimeSpan offset = new TimeSpan(offsetHours, 0, 0);
            DateTimeOffset dateTimeOffset = new DateTimeOffset(dateTime, offset);
            return dateTimeOffset.ToString("yyyy-MM-ddTHH:mm:ss.ffffffzzz");
        }

        /// <summary>
        /// 将 DateTime 格式化为 ISO 8601 字符串（使用系统本地时区）
        /// </summary>
        /// <param name="dateTime">要格式化的 DateTime</param>
        /// <returns>格式化后的字符串</returns>
        public static string FormatDateTimeToIso8601(this DateTime dateTime)
        {
            TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(dateTime);
            DateTimeOffset dateTimeOffset = new DateTimeOffset(dateTime, offset);
            return dateTimeOffset.ToString("yyyy-MM-ddTHH:mm:ss.ffffffzzz");
        }
    }
}
