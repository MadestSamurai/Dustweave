using System;
namespace BD2FiendHunter.Shared {
public sealed class Control {
 public string Session="";public long ExpiresUtcTicks;public bool Enabled;
 public float AttackRange=1.4f,HealFraction=.48f,DodgeInterval=1.4f;public bool Defend=true;
 public void Validate(){if(float.IsNaN(AttackRange)||float.IsNaN(HealFraction)||float.IsNaN(DodgeInterval)||AttackRange<1||AttackRange>6||HealFraction<.1||HealFraction>.8||DodgeInterval<.4||DodgeInterval>5)throw new ArgumentException("Invalid combat settings");}
 public bool Valid(string session,long now){return Session==session&&ExpiresUtcTicks>now&&ExpiresUtcTicks-now<=TimeSpan.FromSeconds(10).Ticks;}
}
}
