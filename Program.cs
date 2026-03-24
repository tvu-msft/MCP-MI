using MCP_MI;

try
{
    string token = TokenProvider.GetIcmMcpApiToken();
    Console.WriteLine("TOKEN_SUCCESS");
    Console.WriteLine(token);
}
catch (Exception ex)
{
    Console.WriteLine("TOKEN_FAILED");
    Console.WriteLine(ex.Message);
}
