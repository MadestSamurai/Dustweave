using System;
using System.Globalization;
using Neo.Unity.Neon;
namespace BD2Daily.Runtime
{
    internal static class SdkIdentity
    {
        // Auth is an exception-caching Lazy<T>. Even a null check invokes its factory.
        internal static bool TryRead(out string accountKey)
        {
            accountKey = "";
            if (!NeonSdk.IsInitialized) return false;
            var auth = NeonSdk.Auth;
            var member = auth == null ? null : auth.LoggedMember;
            if (member != null) accountKey = DailyIdentity.MemberKey(member.MemberId.ToString(CultureInfo.InvariantCulture));
            return true;
        }
    }
}
