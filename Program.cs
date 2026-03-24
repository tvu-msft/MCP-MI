using MCP_MI.Samples;
using MCP_MI;

try
{
    // string token = TokenProvider.GetIcmMcpApiToken();
    // Console.WriteLine("TOKEN_SUCCESS");
    // Console.WriteLine(token);
    await IcmMcpClientSample.RunAsync();
}
catch (Exception ex)
{
    Console.WriteLine("RUN_FAILED");
    Console.WriteLine(ex.Message);
}
