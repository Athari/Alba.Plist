using System;

namespace Alba.Plist
{
    public static class PlistDateConverter
    {
        public const long AppleTimeDifference = 978307200;

        public static long GetAppleTime (long unixTime)
        {
            return unixTime - AppleTimeDifference;
        }

        public static long GetUnixTime (long appleTime)
        {
            return appleTime + AppleTimeDifference;
        }

        public static DateTime ConvertFromAppleTimeStamp (double timestamp)
        {
            return new DateTime(2001, 1, 1, 0, 0, 0, 0).AddSeconds(timestamp);
        }

        public static double ConvertToAppleTimeStamp (DateTime date)
        {
            return Math.Floor((date - new DateTime(2001, 1, 1, 0, 0, 0, 0)).TotalSeconds);
        }
    }
}