using System.Net;
using System.Net.Http.Json;
using Futsal.Contracts;
using Futsal.Web.Services;
namespace Futsal.Tests;
public sealed class PilotLoginTests
{
 sealed class Handler(bool complete):HttpMessageHandler,IHttpClientFactory
 {
  public int Calls;public HttpClient CreateClient(string name)=>new(this,false){BaseAddress=new Uri("https://test.invalid/")};
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Calls++;Assert.Equal("Bearer",request.Headers.Authorization?.Scheme);return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new OnboardingView(0,null,null,complete))});}
 }
 [Theory][InlineData("Owner",false,true,1)][InlineData("Owner",true,false,1)][InlineData("Manager",false,false,0)][InlineData("Cashier",false,false,0)]
 public async Task Login_wizard_only_redirects_Owners_with_unfinished_setup(string role,bool complete,bool expected,int calls)
 {
  using var handler=new Handler(complete);var token=new TokenDto(Guid.NewGuid(),Guid.NewGuid(),role,"test-session",DateTimeOffset.MinValue,"",DateTimeOffset.MinValue);Assert.Equal(expected,await new TokenEndpointClient(handler).NeedsOnboardingAsync(token,default));Assert.Equal(calls,handler.Calls);
 }
}
