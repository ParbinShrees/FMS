using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Deque.AxeCore.Playwright;
using Futsal.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;
namespace Futsal.Tests;
public sealed class PublicAccessibilityTests
{
 [Fact]public async Task Public_pages_load_under_CSP_and_have_no_serious_axe_findings()
 {
  await using var factory=new WebApplicationFactory<Milestone9>().WithWebHostBuilder(b=>b.UseEnvironment("Development").ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{{"Security:KnownProxies:0","127.0.0.1"},{"Security:KnownProxies:1","::1"},{"Api:BaseUrl","http://127.0.0.1:1/"}})));
  factory.UseKestrel(0);using var client=factory.CreateClient(new(){AllowAutoRedirect=false});
  var address=factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
  using var playwright=await Playwright.CreateAsync();await using var browser=await playwright.Chromium.LaunchAsync(new(){Headless=true,Channel="chromium"});await using var context=await browser.NewContextAsync(new(){ViewportSize=new(){Width=390,Height=844},ExtraHTTPHeaders=new Dictionary<string,string>{{"X-Forwarded-For","127.0.0.1"},{"X-Forwarded-Proto","https"}}});
  var page=await context.NewPageAsync();var violations=new List<string>();page.Console+=(_,message)=>{if(message.Text.Contains("violates the following Content Security Policy"))violations.Add(message.Text);};
  foreach(var route in new[]{"/v","/v/my-bookings","/privacy","/terms","/support","/login"})
  {
   var response=await page.GotoAsync(new Uri(new Uri(address),route).ToString());Assert.Equal(200,response!.Status);await page.WaitForTimeoutAsync(500);
   var result=await page.RunAxe();var serious=result.Violations.Where(v=>v.Impact is "serious" or "critical").Select(v=>v.Id+": "+v.Description+" "+string.Join("; ",v.Nodes.Select(n=>System.Text.Json.JsonSerializer.Serialize(n)))).ToList();if(serious.Count>0)Assert.Fail(string.Join("\n",serious));
  }
  if(Environment.GetEnvironmentVariable("RUN_LIGHTHOUSE")=="1")
  {
   var root=new DirectoryInfo(AppContext.BaseDirectory);while(root!=null&&!File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
   var info=new System.Diagnostics.ProcessStartInfo("node","lighthouse.mjs"){WorkingDirectory=Path.Combine(root!.FullName,"tools/browser-checks"),RedirectStandardOutput=true,RedirectStandardError=true};info.Environment["PUBLIC_URL"]=new Uri(new Uri(address),"/v").ToString();info.Environment["CHROME_PATH"]=playwright.Chromium.ExecutablePath;info.Environment["LOCAL_TEST_PROXY"]="true";
   using var process=System.Diagnostics.Process.Start(info)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();Assert.True(process.ExitCode==0,(await output)+"\n"+(await error));
  }
  Assert.Empty(violations);await page.Keyboard.PressAsync("Tab");Assert.NotEqual("BODY",await page.EvaluateAsync<string>("document.activeElement.tagName"));
 }
}
