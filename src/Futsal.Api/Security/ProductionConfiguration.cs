using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using System.Security.Cryptography.X509Certificates;
namespace Futsal.Api.Security;
public static class ProductionConfiguration
{
 public static bool Strong(string? value,int minimum=32)=>value!=null&&value.Length>=minimum&&value.Distinct().Count()>=12&&!new[]{"development","changeme","change-me","password","example","replace","your-secret"}.Any(x=>value.Contains(x,StringComparison.OrdinalIgnoreCase));
 public static void Validate(IConfiguration config,bool production)
 {
  if(!production)return;
  var cs=new NpgsqlConnectionStringBuilder(config.GetConnectionString("FutsalPro")??"");
  if(!Strong(config["Jwt:SigningKey"])||!Strong(cs.Password,20))throw new InvalidOperationException("Production requires strong unique JWT and database credentials.");
  if(string.IsNullOrWhiteSpace(config["DataProtection:KeyPath"])||!Path.IsPathFullyQualified(config["DataProtection:KeyPath"]!))throw new InvalidOperationException("A persistent absolute Data Protection key path is required.");
  var cert=config["DataProtection:CertificatePath"];
  if(string.IsNullOrWhiteSpace(cert)||!File.Exists(cert)||!Strong(config["DataProtection:CertificatePassword"],20))throw new InvalidOperationException("A protected payment encryption certificate is required.");
  using var certificate=X509CertificateLoader.LoadPkcs12FromFile(cert,config["DataProtection:CertificatePassword"]);
  if(!certificate.HasPrivateKey||certificate.NotAfter<=DateTime.UtcNow||certificate.GetRSAPrivateKey()?.KeySize<2048)throw new InvalidOperationException("Payment encryption certificate is invalid or weak.");
  var origins=config.GetSection("Cors:WebOrigins").Get<string[]>()??[];
  if(origins.Length==0||origins.Any(x=>!Uri.TryCreate(x,UriKind.Absolute,out var u)||u.Scheme!="https"||u.AbsolutePath!="/"||!string.IsNullOrEmpty(u.Query)||x.Contains('*')))throw new InvalidOperationException("Production requires explicit HTTPS CORS origins.");
  if((config.GetSection("Security:KnownProxies").Get<string[]>()??[]).Length==0)throw new InvalidOperationException("Production requires known reverse proxy addresses.");
 }
 public static void ConfigureKeys(WebApplicationBuilder builder)
 {
  var keys=builder.Services.AddDataProtection().SetApplicationName("FutsalPro.Api");
  if(builder.Configuration["DataProtection:KeyPath"] is {Length:>0} path)keys.PersistKeysToFileSystem(new DirectoryInfo(path));
  if(builder.Configuration["DataProtection:CertificatePath"] is {Length:>0} cert)keys.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(cert,builder.Configuration["DataProtection:CertificatePassword"]));
 }
}
