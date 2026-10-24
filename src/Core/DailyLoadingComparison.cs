using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dustweave;

// Offline, streaming summary only. Original records are never edited or culled.
// In particular an ETL's existence is not proof of stack coverage or zero loss.
public static class DailyLoadingComparison
{
    sealed class SpanStats { public string Name {get;set;}=""; public long Count {get;set;} public double MaxMs {get;set;} public double TotalMs {get;set;} }
    static string Text(JsonElement row,string name)=>row.TryGetProperty(name,out var p)&&p.ValueKind==JsonValueKind.String?p.GetString()??"":"";
    static long Number(JsonElement row,string name)=>row.TryGetProperty(name,out var p)&&p.TryGetInt64(out long n)?n:0;
    static IEnumerable<string> Files(string directory)
    {
        if(!Directory.Exists(directory))yield break;
        var pending=new Stack<string>();pending.Push(directory);
        while(pending.TryPop(out string? current)){
            DailyCacheStorage.NoLinks(current);
            foreach(string path in Directory.EnumerateFileSystemEntries(current)){
                DailyCacheStorage.NoLinks(path);
                if(Directory.Exists(path))pending.Push(path);else yield return path;
            }
        }
    }
    public static string Analyze(string directory)
    {
        directory=Path.GetFullPath(directory);DailyCacheStorage.NoLinks(directory);
        var groups=new List<object>();
        foreach(string group in Directory.EnumerateDirectories(directory)){
            if(!File.Exists(Path.Combine(group,"session.json")))continue;
            DailyCacheStorage.NoLinks(group);
            var errors=new List<string>();
            JsonNode? Read(string relative){try{return DailyJson.TryRead<JsonNode>(Path.Combine(group,relative));}catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){errors.Add(relative+": "+e.Message);return null;}}
            var session=Read("session.json");var stats=new Dictionary<string,SpanStats>();
            var pending=new Dictionary<string,object>();var kinds=new Dictionary<string,long>();var builds=new HashSet<string>();var unavailable=new HashSet<string>();
            var traceCoverage=new List<object>();
            var captureStatus=Read("status.json");
            long expectedUntil=File.Exists(Path.Combine(group,"STOP"))?File.GetLastWriteTimeUtc(Path.Combine(group,"STOP")).Ticks:0;
            long invalid=0,dropped=0,rows=0,totalBytes=0,nativeBytes=0,traceBytes=0,firstMenu=0;int fileCount=0,traces=0;
            foreach(string file in Files(group)){
                fileCount++;long bytes=new FileInfo(file).Length;totalBytes+=bytes;if(Path.GetExtension(file)==".etl")nativeBytes+=bytes;
                if(!Path.GetFileName(file).StartsWith("runtime-",StringComparison.Ordinal)||Path.GetExtension(file)!=".jsonl")continue;
                traces++;traceBytes+=bytes;double frequency=0;long fileDrops=0,firstUtc=0,lastUtc=0;string closedReason="";bool closed=false;
                using var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var reader=new StreamReader(input);
                string? line;
                while((line=reader.ReadLine())!=null){
                    try{
                        using var json=JsonDocument.Parse(line);var row=json.RootElement;rows++;
                        string kind=Text(row,"Kind"),name=Text(row,"Name");kinds[kind]=kinds.GetValueOrDefault(kind)+1;
                        long utc=Number(row,"Utc");fileDrops=Math.Max(fileDrops,Number(row,"Dropped"));
                        if(utc>0){if(firstUtc==0||utc<firstUtc)firstUtc=utc;lastUtc=Math.Max(lastUtc,utc);}
                        if(kind=="closed"){closed=true;closedReason=Text(row,"Reason");}
                        if(kind=="clock"&&name.StartsWith("frequency=",StringComparison.Ordinal))double.TryParse(name[10..],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out frequency);
                        if(kind=="game-build")builds.Add(name);
                        if(kind.Contains("unavailable",StringComparison.Ordinal))unavailable.Add(kind+": "+name);
                        if(kind=="daily-surfaces"&&name.Split([',',':']).Contains("MenuUI")&&(firstMenu==0||utc<firstMenu))firstMenu=utc;
                        if(kind is not ("enter" or "leave"))continue;
                        long started=Number(row,"Started");string key=file+"|"+Number(row,"Thread")+"|"+started+"|"+name;
                        if(kind=="enter"){pending[key]=new{name,utc,thread=Number(row,"NativeThread"),trace=Path.GetRelativePath(group,file)};continue;}
                        pending.Remove(key);if(frequency<=0)continue;
                        double ms=(Number(row,"Stamp")-started)*1000d/frequency;if(ms<0){invalid++;continue;}
                        if(!stats.TryGetValue(name,out var item))stats[name]=item=new(){Name=name};
                        item.Count++;item.TotalMs+=ms;item.MaxMs=Math.Max(item.MaxMs,ms);
                    }catch(Exception e)when(e is JsonException or InvalidOperationException or FormatException){invalid++;}
                }
                dropped+=fileDrops;
                traceCoverage.Add(new{trace=Path.GetRelativePath(group,file),firstUtcTicks=firstUtc,lastUtcTicks=lastUtc,
                    closed,closedReason,endedSecondsBeforeCollectorStop=lastUtc>0&&expectedUntil>0?Math.Max(0,(expectedUntil-lastUtc)/(double)TimeSpan.TicksPerSecond):(double?)null,
                    endsEarly=lastUtc>0&&expectedUntil>0&&expectedUntil-lastUtc>TimeSpan.FromSeconds(20).Ticks,
                    interpretation="An early end is a coverage gap to inspect, not proof of a game stall. Check process exit, stop reason, lease/read errors and native trace."});
            }
            groups.Add(new{directory=Path.GetFileName(group),mode=session?["mode"]?.GetValue<string>(),deep=session?["deep"]?.GetValue<bool>(),
                files=fileCount,bytes=totalBytes,nativeBytes,traceBytes,traces,rows,invalidRows=invalid,dropped,traceCoverage,captureStatus,
                firstMenuUtc=firstMenu>0?new DateTime(firstMenu,DateTimeKind.Utc).ToString("O"):null,
                actualGameBuilds=builds,hostGame=Read("host-game-files.json"),coverage=Read("coverage.json"),result=Read("group-result.json"),
                system=Read("system-trace-status.json"),nativeValidation=Read("analysis/native-coverage.json"),
                kinds,unavailable,longestBoundaries=stats.Values.OrderByDescending(s=>s.MaxMs).Take(40),unmatchedEntries=pending.Values,errors,
                interpretation="Measured boundaries are not causality. Compare the same route and warm-up order. An ETL requires independent stack/loss validation; failed groups remain included."});
        }
        string output=Path.Combine(directory,"comparison.json");DailyJson.Write(output,new{createdUtc=DateTimeOffset.UtcNow,groups});return output;
    }
}
