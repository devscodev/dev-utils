using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

static partial class Jira
{
    [GeneratedRegex("[^a-zA-Z0-9-]")]
    private static partial Regex Branch_Invalid_Char_Pattern();

    public static string Get_New_Branch_Name(string jira_key)
    {
        //Any CLI output from this utility should be yellow
        var current_cli_text_color = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;

        try
        {
            var config = Config.Load();
            if (!jira_key.Contains('-'))
            {
                var default_prefix = Config.Load().JIRA_Default_Issue_ID_Prefix;
                jira_key = default_prefix + "-" + jira_key;
            }

            using var client = new HttpClient();
            client.BaseAddress = new Uri(config.JIRA_Base_URL);
            client.DefaultRequestHeaders.Authorization = new
            (
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(Environment.UserName + ":" + config.JIRA_Password))
            );

            var response = client.GetAsync($"/rest/api/latest/issue/{jira_key}?fields=summary").Result;

            if (!response.IsSuccessStatusCode)
            {
                throw new Log.Exception($"Got non-success status code {(int)response.StatusCode} {response.StatusCode} while looking up JIRA issue {jira_key}\nURL: {response.RequestMessage!.RequestUri}\nUsername: {Environment.UserName}");
            }

            using var stream = response.Content.ReadAsStreamAsync().Result;
            using var doc = JsonDocument.Parse(stream);
            var summary = doc.RootElement.GetProperty("fields").GetProperty("summary").GetString()
                ?? throw new Log.Exception($"Failed to parse successful response body while looking up JIRA issue {jira_key}.\nURL: {response.RequestMessage!.RequestUri}");

            return jira_key + "-" + Branch_Invalid_Char_Pattern().Replace(summary.Replace(' ', '-'), "");
        }
        finally
        {
            Console.ForegroundColor = current_cli_text_color;
        }
    }
}