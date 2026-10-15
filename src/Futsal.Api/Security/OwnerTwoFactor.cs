using System.Security.Cryptography;
using System.Text;
using Futsal.Domain.Identity;
using Futsal.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;
namespace Futsal.Api.Security;
public sealed class OwnerTwoFactor(IDataProtectionProvider provider,IClock clock)
{
 private readonly IDataProtector protector=provider.CreateProtector("FutsalPro.OwnerTotp.v1");
 public string NewSecret()=>Base32(RandomNumberGenerator.GetBytes(20));
 public string Protect(string secret)=>protector.Protect(secret);
 public static string Hash(string code)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
 public bool Verify(AppUser user,string? code,bool recovery=true)
 {
  if(string.IsNullOrWhiteSpace(code))return false;
  if(recovery&&user.RecoveryCodeHashes.Contains(Hash(code))){user.RecoveryCodeHashes=user.RecoveryCodeHashes.Where(x=>x!=Hash(code)).ToArray();return true;}
  if(user.TotpSecret==null||code.Length!=6||!code.All(char.IsAsciiDigit))return false;
  var secret=Decode(protector.Unprotect(user.TotpSecret));long now=clock.UtcNow.ToUnixTimeSeconds()/30;
  for(long step=now-1;step<=now+1;step++)if(step>user.TotpLastStep&&CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(secret,step)),Encoding.ASCII.GetBytes(code))){user.TotpLastStep=step;return true;}
  return false;
 }
 public static string Code(byte[] secret,long counter)
 {
  Span<byte> bytes=stackalloc byte[8];System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes,counter);var hash=HMACSHA1.HashData(secret,bytes);var offset=hash[^1]&15;var value=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset,4))&0x7fffffff;return (value%1000000).ToString("D6",System.Globalization.CultureInfo.InvariantCulture);
 }
 private const string Alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
 public static string Base32(byte[] bytes){int buffer=0,bits=0;var result=new StringBuilder();foreach(var b in bytes){buffer=(buffer<<8)|b;bits+=8;while(bits>=5){result.Append(Alphabet[(buffer>>(bits-5))&31]);bits-=5;}}if(bits>0)result.Append(Alphabet[(buffer<<(5-bits))&31]);return result.ToString();}
 private static byte[] Decode(string text){int buffer=0,bits=0;var output=new List<byte>();foreach(var c in text){buffer=(buffer<<5)|Alphabet.IndexOf(c);bits+=5;if(bits>=8){output.Add((byte)(buffer>>(bits-8)));bits-=8;}}return output.ToArray();}
}
