using System;

namespace AION_2_Companion.Core.Integrity;

internal static class IntegrityTrust
{
    internal const string KeyId = "nexforge-release-2026-01";

    internal static ReadOnlyMemory<byte> PublicKey => Convert.FromBase64String(
        "MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAsj9IO/or02JlcuDfVRDwtL2Lel2o0v7BWn79X9cCdphhO451/KkHaNN94maQ3o7l7KDKI/4KONYLopvQXWEkdBOhB8+3xZ5oBYdkGgWCNamyJnzLazYEwa5XbhPihCK5z1+tM0B/iQnfMxVgMnNgj/1oiYX5Oux09gfHjblUFUigYSWdFNZI6Rf+MMYct/xbSeLS61nS0+9Ffrzym18sjA/P7m7tuVjyYpnlNO8u98dJISSbjjBQSpOKIUdKIQAREGVpoa4MH2v+qG9F0sX3ey/o4iXzPn1+FOun4otZLMXl6p5ztacFDUUfhquN6S+T9oMNDoGrPw4hvpQw5iJlsSpVmB27Uuc/PzaFstccgwUjNjNCfQuBf51fm8O0t0/8/yvsCfyQI2r3gFUJ6d6MDwxfltIqpFf2zXw9H3uOVGjkLNRYt6+kLbMWxLyh1Uzz9xwmkOWUqiTzoqdunjaTfQuSyF6W7bKkDnDdlTGK4zhW8BotL8RNCDWVR2H2cyhBAgMBAAE=");
}
