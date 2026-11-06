using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Google.Protobuf;
namespace BD2Daily.Runtime
{
    internal sealed class InventoryRuntime
    {
        private string completed="";
        private long clearAfter;
        private FieldInfo[] fields;
        internal void Tick(DailySnapshot snapshot)
        {
            var request=LocalStorage.Read<ToolInventoryRequest>("tool-inventory-request.json");
            long now=DateTime.UtcNow.Ticks;
            if(clearAfter>0&&(now>clearAfter||(request!=null&&request.Id==completed&&request.ExpiresUtcTicks<=now))){LocalStorage.Write("tool-inventory-response.json",new ToolInventoryResponse());clearAfter=0;}
            if(request==null||request.Id==completed||request.Id.Length!=32||request.ExpiresUtcTicks<=now||request.ExpiresUtcTicks>now+TimeSpan.FromSeconds(30).Ticks)return;
            completed=request.Id;
            var response=new ToolInventoryResponse{Id=request.Id,ProcessId=snapshot.ProcessId,ProcessStartTicks=snapshot.ProcessStartTicks,
                AccountKey=snapshot.AccountKey,PlayerKey=snapshot.PlayerKey,InstanceId=snapshot.InstanceId,PlayerName=snapshot.PlayerName,AtUtcTicks=now};
            try
            {
                if(snapshot.State!="identified"||request.ProcessId!=snapshot.ProcessId||request.ProcessStartTicks!=snapshot.ProcessStartTicks||request.InstanceId!=snapshot.InstanceId||request.AccountKey!=snapshot.AccountKey||request.PlayerKey!=snapshot.PlayerKey)
                    throw new InvalidOperationException("Inventory identity changed; refresh the current account.");
                if(InventoryBindings.Error!="")throw new InvalidOperationException(InventoryBindings.Error);
                if(fields==null){fields=new FieldInfo[4];for(int i=0;i<4;i++)fields[i]=typeof(Proto.Net.UserDBInfo).Assembly.GetType(InventoryBindings.Types[i],true).GetField(InventoryBindings.Fields[i],BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);}
                var formatter=new JsonFormatter(new JsonFormatter.Settings(true));
                var components=new List<string>();string[] names={"characters","awake","costume","equipment"};int bytes=0;
                for(int i=0;i<4;i++)
                {
                    var cache=fields[i].GetValue(null) as IDictionary;
                    if(cache==null||cache.Count>20000)throw new InvalidOperationException("Inventory cache is unavailable or too large.");
                    var rows=new List<string>();
                    foreach(var value in cache.Values){var item=value as IMessage;if(item==null)throw new InvalidOperationException("Inventory row type changed.");string text=formatter.Format(item);bytes+=System.Text.Encoding.UTF8.GetByteCount(text);if(bytes>4*1024*1024)throw new InvalidOperationException("Inventory snapshot is too large.");rows.Add(text);}
                    components.Add("{\"name\":\""+names[i]+"\",\"rows\":["+string.Join(",",rows.ToArray())+"]}");
                }
                response.Json="{\"components\":["+string.Join(",",components.ToArray())+"]}";
            }
            catch(Exception error){response.Error=error.GetBaseException().Message;}
            LocalStorage.Write("tool-inventory-response.json",response);
            clearAfter=request.ExpiresUtcTicks;
        }
    }
}
