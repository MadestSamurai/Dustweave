using System.Globalization;
using BD2InfiniteGacha.Localization;
namespace BD2InfiniteGacha;

// Presentation metadata must never be part of the automation pool identity.
public sealed class PoolChoice
{
    public Pool Pool {get;}
    public DateTimeOffset? EndTime {get;}
    public string Title {get;}
    public string Subtitle {get;}
    public string Detail {get;}
    private PoolChoice(Pool pool,int current,LanguageCatalog language,TimeZoneInfo zone)
    {
        Pool=pool;
        if(pool.EndTimeUnixMilliseconds>0)
            try{EndTime=TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(pool.EndTimeUnixMilliseconds),zone);}
            catch(ArgumentOutOfRangeException){ /* Missing/invalid dates stay last; never invent an expiry. */ }
        Title=EndTime.HasValue?language.Text("截止 ")+EndTime.Value.ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture):language.Text("截止时间未提供");
        Subtitle="#"+pool.Id+(pool.Id==current?" · "+language.Text("当前结果"):"");
        Detail=pool.Name+"\n"+(EndTime.HasValue?language.Text("本机时间")+" UTC"+EndTime.Value.ToString("zzz",CultureInfo.InvariantCulture):language.Text("游戏未提供该卡池的截止时间"));
    }
    public static PoolChoice[] Create(IEnumerable<Pool> pools,int current,LanguageCatalog language,TimeZoneInfo zone)=>pools.Select(p=>new PoolChoice(p,current,language,zone)).OrderByDescending(p=>p.EndTime.HasValue).ThenByDescending(p=>p.EndTime).ThenByDescending(p=>p.Pool.Id).ToArray();
}
