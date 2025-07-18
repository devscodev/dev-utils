using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

partial record class Config
{
    [GeneratedRegex("[a-zA-Z]+(-?[a-zA-Z0-9]+)*")]
    private static partial Regex JIRA_Issue_ID_Prefix_Pattern();

    public required string JIRA_Base_URL { get; init; }
    public required string JIRA_Password { get; init; }
    public required string JIRA_Default_Issue_ID_Prefix { get; init; }

    private static Config? Value = null;

    public static Config Load()
    {
        if (Value is not null) return Value;

        var rc_file_path = Environment.ExpandEnvironmentVariables("%userprofile%\\.sdnrc");

        if (!File.Exists(rc_file_path))
        {
            throw new Log.Exception("Config file not found. Use \"config\" command to resolve.");
        }

        Config? config = null;

        try
        {
            config = JsonSerializer.Deserialize<Config>(File.ReadAllText(rc_file_path));
        }
        catch (Exception) { }

        if (config is null) throw new Log.Exception("Failed to deserialize config file. Use \"config\" command to resolve.");

        if (!Is_Valid_URL(config.JIRA_Base_URL))
        {
            throw new Log.Exception("Config file contains invalid JIRA_Base_URL. Use \"config\" command to resolve.\nCurrent value: " + config.JIRA_Base_URL);
        }

        if (!Try_Decrypt_Password(config.JIRA_Password, out var plaintext_password))
        {
            throw new Log.Exception($"Config file contains invalid JIRA_Password. Use \"config\" command to resolve.");
        }

        if (!Is_Valid_Prefix(config.JIRA_Default_Issue_ID_Prefix))
        {
            throw new Log.Exception($"Config file contains invalid JIRA_Default_Issue_ID_Prefix. Use \"config\" command to resolve.");
        }

        return Value = config with { JIRA_Password = plaintext_password };
    }

    private static bool Is_Valid_URL(string? url)
    {
        return url is not null && Uri.TryCreate(url, UriKind.Absolute, out Uri? uriResult)
            && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    }

    private static bool Try_Decrypt_Password(string? encrypted_password, out string decrypted_password)
    {
        if (encrypted_password is null)
        {
            decrypted_password = "";
            return false;
        }
        
        try
        {
            var encrypted_password_bytes = Convert.FromBase64String(encrypted_password);
#pragma warning disable CA1416 // Validate platform compatibility
            var decrypted_password_bytes = ProtectedData.Unprotect(encrypted_password_bytes, null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416 // Validate platform compatibility
            decrypted_password = Encoding.UTF8.GetString(decrypted_password_bytes);
            return true;
        }
        catch (Exception)
        {
            decrypted_password = "";
            return false;
        }
    }

    public static void Update_File()
    {
        var current_CLI_text_color = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;

        try
        {
            var rc_file_path = Environment.ExpandEnvironmentVariables("%userprofile%\\.sdnrc");

            string? existing_URL = null;
            string? existing_password = null;
            string? existing_default_prefix = null;

            ///////////////////////////////////////////////////////////////////////////////////
            // Get existing config values
            ///////////////////////////////////////////////////////////////////////////////////

            if (File.Exists(rc_file_path))
            {
                Config? config = null;

                try
                {
                    config = JsonSerializer.Deserialize<Config>(File.ReadAllText(rc_file_path));
                }
                catch (Exception) { }

                if (config is not null)
                {
                    existing_URL = Is_Valid_URL(config.JIRA_Base_URL)
                        ? config.JIRA_Base_URL
                        : null;

                    existing_password = Try_Decrypt_Password(config.JIRA_Password, out _)
                        ? config.JIRA_Password
                        : null;

                    existing_default_prefix = Is_Valid_Prefix(config.JIRA_Default_Issue_ID_Prefix)
                        ? config.JIRA_Default_Issue_ID_Prefix
                        : null;
                }
            }

            string new_URL;
            string new_password;
            string new_default_prefix;

            ///////////////////////////////////////////////////////////////////////////////////
            // Update URL
            ///////////////////////////////////////////////////////////////////////////////////

            if (existing_URL is not null)
            {
                Console.WriteLine("Existing JIRA base URL: " + existing_URL);
                Console.Write("New (optional) > ");
                var URL = Console.ReadLine();
                if (URL is not null && URL.Length != 0)
                {
                    if (Is_Valid_URL(URL)) new_URL = URL;
                    else throw new Log.Exception("Invalid URL provided");
                }
                else
                {
                    new_URL = existing_URL;
                }
            }
            else
            {
                Console.Write("JIRA base URL (required) > ");
                var URL = Console.ReadLine();
                if (!Is_Valid_URL(URL)) throw new Log.Exception("Invalid URL provided");
                else new_URL = URL!;
            }

            ///////////////////////////////////////////////////////////////////////////////////
            // Update password
            ///////////////////////////////////////////////////////////////////////////////////

            if (existing_password is not null)
            {
                Console.Write($"JIRA password for {Environment.UserName} (optional) > ");
                var password = Prompt_For_Password();
                if (password is not null && password.Length != 0)
                {
                    new_password = password;
                }
                else
                {
                    new_password = existing_password;
                }
            }
            else
            {
                Console.Write($"JIRA password for {Environment.UserName} (required) > ");
                var password = Prompt_For_Password();
                if (password is null || password.Length == 0)
                {
                    throw new Log.Exception("Password is required");
                }
                else
                {
                    new_password = password;
                }
            }

            ///////////////////////////////////////////////////////////////////////////////////
            // Update default prefix
            ///////////////////////////////////////////////////////////////////////////////////

            if (existing_default_prefix is not null)
            {
                Console.WriteLine("Existing default JIRA issue ID prefix: " + existing_default_prefix);
                Console.Write("New (optional) > ");
                var prefix = Console.ReadLine();
                if (prefix is not null && prefix.Length != 0)
                {
                    if (Is_Valid_Prefix(prefix)) new_default_prefix = prefix;
                    else throw new Log.Exception("Invalid default JIRA issue ID prefix provided");
                }
                else
                {
                    new_default_prefix = existing_default_prefix;
                }
            }
            else
            {
                Console.Write("Default JIRA issue ID prefix (required) > ");
                var prefix = Console.ReadLine();
                if (!Is_Valid_Prefix(prefix)) throw new Log.Exception("Invalid default JIRA issue ID prefix provided");
                else new_default_prefix = prefix!;
            }

            ///////////////////////////////////////////////////////////////////////////////////
            // Save new config
            ///////////////////////////////////////////////////////////////////////////////////

            var decrypted_password_bytes = Encoding.UTF8.GetBytes(new_password);

#pragma warning disable CA1416 // Validate platform compatibility
            var encrypted_password_bytes = ProtectedData.Protect(decrypted_password_bytes, null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416 // Validate platform compatibility

            var new_encrypted_config = new Config
            {
                JIRA_Base_URL = new_URL,
                JIRA_Password = Convert.ToBase64String(encrypted_password_bytes),
                JIRA_Default_Issue_ID_Prefix = new_default_prefix
            };

            File.WriteAllText(rc_file_path, JsonSerializer.Serialize(new_encrypted_config));
        }
        finally
        {
            Console.ForegroundColor = current_CLI_text_color;
        }
    }

    private static bool Is_Valid_Prefix(string? prefix)
    {
        return prefix is not null && JIRA_Issue_ID_Prefix_Pattern().IsMatch(prefix);
    }

    /// <summary>
    /// Prompts the user for a password on the CLI, securely replacing their input with * as they type
    /// </summary>
    private static string Prompt_For_Password()
    {
        var input = new StringBuilder();
        ConsoleKeyInfo key;

        do
        {
            key = Console.ReadKey(intercept: true);

            if (!char.IsControl(key.KeyChar))
            {
                input.Append(key.KeyChar);
                Console.Write("*");
            }
            else
            {
                if (key.Key == ConsoleKey.Backspace && input.Length > 0)
                {
                    input.Remove(input.Length - 1, 1);
                    Console.Write("\b \b");
                }
            }
        }
        while (key.Key != ConsoleKey.Enter);

        Console.WriteLine();

        return input.ToString();
    }
}