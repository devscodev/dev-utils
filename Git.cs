using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

static partial class Git
{
    [GeneratedRegex(@"HEAD branch: (\S+)")]
    private static partial Regex Head_Branch_Pattern();

    /// <summary>
    /// Validates repo state and returns the current and default branch names
    /// </summary>
    /// <param name="allow_unpushed_on_current">If false, exception will be thrown if there are unpushed commits on the current branch</param>
    /// <param name="allow_uncommitted_changes">If false (default), exception will be thrown if there are uncommitted changes</param>
    public static (string default_branch, string current_branch) Get_State(bool allow_unpushed_on_current, bool allow_uncommitted_changes = false)
    {
        Log.Step = "Fetch, prune, and validate GIT state";

        var current_branch = Run("rev-parse --abbrev-ref HEAD");

        string default_branch = Head_Branch_Pattern().Match(Run("remote show origin")) switch
        {
            { Success: true } m => m.Groups[1].Value,
            _ => throw new Log.Exception("Could not identify default branch")
        };

        if (!allow_uncommitted_changes)
        {
            var has_uncommitted_changes = !string.IsNullOrWhiteSpace(Run("status --porcelain"));
            if (has_uncommitted_changes) throw new Log.Exception("Uncommitted changes detected. Stash or commit before proceeding.");
        }

        Run("fetch --prune");

        if (current_branch != default_branch)
        {
            //Verify that the current branch does not exist in remote
            if (Run_Safe($"show-ref refs/remotes/origin/{current_branch}").is_success)
            {
                throw new Log.Exception($"Current branch '{current_branch}' already exists in remote. Delete it from remote before proceeding.");
            }
        }

        if (Has_Unpushed_Commits(default_branch, default_branch))
        {
            throw new Log.Exception($"Unpushed commits detected on default branch '{default_branch}'. Commits should never be made on the default branch. Please remediate manually.");
        }

        if (default_branch != current_branch && !allow_unpushed_on_current && Has_Unpushed_Commits(current_branch, default_branch))
        {
            throw new Log.Exception($"Unpushed commits detected on current branch '{current_branch}'. Either close the branch or rerun this command with the --force option.");
        }

        return (default_branch, current_branch);
    }

    /// <summary>
    /// Run a GIT command, returning the stdout, stderr, and success state
    /// </summary>
    public static (bool is_success, string stdout, string stderr) Run_Safe(string args)
    {
        var launch_info = new ProcessStartInfo("git", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        var process = Process.Start(launch_info)!;
        process.WaitForExit();
        string output = process.StandardOutput.ReadToEnd().Trim();
        string error = process.StandardError.ReadToEnd().Trim();

        return (process.ExitCode == 0, output, error);
    }

    /// <summary>
    /// Run a GIT command, returning the stdout or throwing for non-zero exit code
    /// </summary>
    public static string Run(string args)
    {
        var (is_success, output, error) = Run_Safe(args);
    
        if (!is_success)
        {
            var err_msg = new StringBuilder();
            err_msg.AppendLine("Command: git " + args);
            if (!string.IsNullOrWhiteSpace(output)) err_msg.AppendLine(output);
            if (!string.IsNullOrWhiteSpace(error)) err_msg.AppendLine(error);
            throw new Log.Exception(err_msg.ToString());
        }

        return output;
    }

    private static bool Has_Unpushed_Commits(string target_branch, string default_branch)
    {
        var has_remote_branch = Run_Safe($"rev-parse --abbrev-ref {target_branch}@{{u}}").is_success;

        if (has_remote_branch)
        {
            var unpushed_commit_list = Run($"rev-list {target_branch}@{{u}}..{target_branch}");
            return !string.IsNullOrWhiteSpace(unpushed_commit_list);
        }
        else
        {
            var last_target_local_hash = Run($"rev-parse {target_branch}");
            var last_default_remote_hash = Run($"rev-parse origin/{default_branch}");
            return last_target_local_hash != last_default_remote_hash;
        }
    }
}