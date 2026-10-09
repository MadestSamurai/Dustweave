using Dustweave;
if(args.Length==4&&args[0]=="--drop-model"){var drops=DailyTradeDrops.Build(args[1],DailyTradeCatalog.Read(args[2]));DailyJson.Write(args[3],drops);Console.WriteLine("Built weighted cartridge drop model; no game input.");return;}
if(args.Length==3&&args[0]=="--steal-catalog"){var index=DailyStealCatalog.Build(args[1]);DailyJson.Write(args[2],index);Console.WriteLine($"Indexed {index["packs"]!.AsArray().Count} cartridges; .NET, no game input.");return;}
if(args.Length!=3)throw new ArgumentException("<exported-tables> <client-mvid> <catalog-output>");
var catalog=DailyTradeCatalog.Build(args[0],File.Exists(args[1])?DailyTradeCatalog.Mvid(args[1]):args[1]);
if(File.Exists(args[1]))DailyTradeCatalog.ValidateFiles(catalog,args[1],DailyTradeData.Database());
DailyJson.Write(args[2],catalog);
Console.WriteLine($"Trade catalog: {catalog["items"]!.AsArray().Count} items, {catalog["offers"]!.AsArray().Count} offers, {catalog["recipes"]!.AsArray().Count} recipes; .NET, no game input.");
Console.WriteLine("Catalog fingerprint: "+DailyTradeCatalog.Fingerprint(catalog));

