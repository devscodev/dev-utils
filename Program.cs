using System.CommandLine;

var app = new RootCommand() { new Option<bool>("--lib", "-l") };

Add_Sync_Action(app);
Add_Reset_Action(app);
Add_Switch_Action(app);
Add_Stage_Action(app);
Add_Unstage_Action(app);
Add_Commit_Action(app);
Add_Close_Action(app);
Add_Config_Action(app);

var parsed_root_args = app.Parse(args);
if (parsed_root_args.GetValue<bool>("--lib")) Environment.CurrentDirectory = ".\\Library";
int exit_code;
try
{
    exit_code = parsed_root_args.Invoke();
}
finally
{
    if (parsed_root_args.GetValue<bool>("--lib")) Environment.CurrentDirectory = "..\\";
}
return exit_code;

void Add_Sync_Action(RootCommand app)
{
    var command = new Command("sync", "Checkout default, pull, checkout current, and rebase default into current");

    command.SetAction(parsed_args => Log.Capture(() =>
    {
        var (default_branch, current_branch) = Git.Get_State(allow_unpushed_on_current: true);

        if (current_branch == default_branch)
        {
            Log.Step = $"Merge 'origin/{default_branch}' into '{default_branch}' (--ff-only)";

            //merge --ff-only will fail if conflicts exist. It will not switch into
            //conflict resolution mode, so no harm done
            Git.Run($"merge --ff-only origin/{default_branch}");
        }
        else
        {
            Log.Step = $"Switch to '{default_branch}'";

            //Uncommitted changes are blocked by Git.Get_State, so this will not fail
            Git.Run($"checkout {default_branch}");

            try
            {
                Log.Step = $"Merge 'origin/{default_branch}' into '{default_branch}' (--ff-only)";

                //merge --ff-only will fail if conflicts exist. It will not switch into
                //conflict resolution mode. So if it fails then all we have to do is
                //switch back to the current branch.
                Git.Run($"merge --ff-only origin/{default_branch}");
            }
            catch (Exception)
            {
                //Uncommitted changes are blocked by Git.Get_State and the state was left
                //untouched by the failed merge, so this will not fail.
                Git.Run_Safe($"checkout {current_branch}");
                throw;
            }

            Log.Step = $"Switch back to '{current_branch}'";

            //Uncommitted changes are blocked by Git.Get_State and the state cleanly
            //updated by the successful merge, so this will not fail.
            Git.Run($"checkout {current_branch}");

            Log.Step = $"Rebase '{default_branch}' into '{current_branch}'";

            //This command could fail. If so, it will shift us into rebase conflict-resolution
            //mode. Rebase recreates any unpushed commits on top of default_branch in order.
            //If recreating a commit leads to a conflict, it will pause in conflict-resolution 
            //mode, wait for the user to resolve the conflicts and run "rebase --continue", and
            //then move on to the next commit. Alternatively, the user can run "rebase --abort"
            //to terminate the process.
            //Since this is the last command, we will let the user handle it normally.
            Git.Run($"rebase {default_branch}");
        }
    }));

    app.Add(command);
}

void Add_Reset_Action(RootCommand app)
{
    var command = new Command("reset", "Checkout default and pull. Use --force if you want to leave unpushed changes on the current branch.")
    {
        new Option<bool>("--force", "-f")
    };

    command.SetAction(parsed_args => Log.Capture(() =>
    {
        var (default_branch, current_branch) = Git.Get_State(parsed_args.GetValue<bool>("--force"));

        if (current_branch != default_branch)
        {
            Log.Step = $"Switch to '{default_branch}'";

            //Uncommitted changes are blocked by Git.Get_State, so this will not fail
            Git.Run($"checkout {default_branch}");

            try
            {
                Log.Step = $"Merge 'origin/{default_branch}' into '{default_branch}' (--ff-only)";

                //merge --ff-only will fail if conflicts exist. It will not switch into
                //conflict resolution mode. So if it fails then all we have to do is
                //switch back to the current branch.
                Git.Run($"merge --ff-only origin/{default_branch}");
            }
            catch (Exception)
            {
                //Uncommitted changes are blocked by Git.Get_State and the state was left
                //untouched by the failed merge, so this will not fail.
                Git.Run_Safe($"checkout {current_branch}");
                throw;
            }
        }
        else
        {
            Log.Step = $"Merge 'origin/{default_branch}' into '{default_branch}' (--ff-only)";

            //merge --ff-only will fail if conflicts exist. It will not switch into
            //conflict resolution mode. Since this is the only command, there is nothing
            //to undo
            Git.Run($"merge --ff-only origin/{default_branch}");
        }
    }));

    app.Add(command);
}

void Add_Switch_Action(RootCommand app)
{
    var command = new Command("switch", "Checkout default, pull, and checkout a new branch. Use --force if you want to leave unpushed changes on the current branch.")
    {
        new Argument<string>("JIRA-ID") { Description = "New branch name will be derived from this JIRA ID and associated issue summary. If value does not contain a dash, \"SEDONA-\" will be prefixed automatically." }
    };

    command.SetAction(parsed_args => Log.Capture(() =>
    {
        var (default_branch, current_branch) = Git.Get_State(allow_unpushed_on_current: true);

        Log.Step = "Get new branch name from JIRA";

        var new_branch = Jira.Get_New_Branch_Name(parsed_args.GetValue<string>("JIRA-ID")!);

        Log.Step = "Verify new branch does not exist in remote";

        if (Git.Run_Safe($"show-ref refs/remotes/origin/{new_branch}").is_success)
        {
            throw new Log.Exception($"New branch '{new_branch}' already exists in remote. Delete it from remote before proceeding.");
        }

        Log.Step = $"Switch to '{default_branch}'";

        //Uncommitted changes are blocked by Git.Get_State, so this will not fail
        Git.Run($"checkout {default_branch}");

        bool branch_exists;

        try
        {
            Log.Step = $"Merge 'origin/{default_branch}' into '{default_branch}' (--ff-only)";

            //merge --ff-only will fail if conflicts exist. It will not switch into
            //conflict resolution mode. So if it fails then all we have to do is
            //switch back to the current branch.
            Git.Run($"merge --ff-only origin/{default_branch}");

            Log.Step = "Check if target branch already exists locally";

            //This should not ever fail, but if it does then its fine because it doesn't
            //modify anything
            branch_exists = Git.Run_Safe($"show-ref --verify --quiet refs/heads/{new_branch}").is_success;
        }
        catch (Exception)
        {
            //Uncommitted changes are blocked by Git.Get_State and the state was left
            //untouched by the failed merge, so this will not fail.
            Git.Run_Safe($"checkout {current_branch}");
            throw;
        }

        if (!branch_exists)
        {
            Log.Step = $"Create new branch '{new_branch}'";

            //This should not ever fail since we know the branch does not exist
            Git.Run($"checkout -b {new_branch}");
        }
        else
        {
            Log.Step = $"Switch to existing '{new_branch}'";

            //This should not ever fail since we know the branch does exist
            Git.Run($"checkout {new_branch}");

            Log.Step = $"Rebase '{default_branch}' into '{current_branch}'";

            //This command could fail. If so, it will shift us into rebase conflict-resolution
            //mode. Rebase recreates any unpushed commits on top of default_branch in order.
            //If recreating a commit leads to a conflict, it will pause in conflict-resolution 
            //mode, wait for the user to resolve the conflicts and run "rebase --continue", and
            //then move on to the next commit. Alternatively, the user can run "rebase --abort"
            //to terminate the process.
            //Since this is the last command, we will let the user handle it normally.
            Git.Run($"rebase {default_branch}");
        }
    }));

    app.Add(command);
}

void Add_Stage_Action(RootCommand app)
{
    var command = new Command("stage", "Stage changes. Accepts standard GIT file globs. Stages all changes if no globs are provided.")
    {
        new Argument<string[]>("globs")
    };

    command.SetAction(parsed_args => Log.Capture(() =>
    {
        var (default_branch, current_branch) = Git.Get_State(allow_unpushed_on_current: true, allow_uncommitted_changes: true);

        if (default_branch == current_branch)
        {
            throw new Log.Exception($"Cannot stage/unstage on default branch '{default_branch}'");
        }

        var globs = parsed_args.GetValue<string[]>("globs")!;

        Log.Step = globs.Length == 0 ? "Add all changes" : $"Add specified changes";
        Git.Run($"add {(globs.Length == 0 ? "." : string.Join(' ', globs))}");
    }));

    app.Add(command);
}

void Add_Unstage_Action(RootCommand app)
{
    var command = new Command("unstage", "Unstage changes. Accepts standard GIT file globs. Unstages all changes if no globs are provided.")
    {
        new Argument<string[]>("globs")
    };

    command.SetAction(parsed_args => Log.Capture(() =>
    {
        var (default_branch, current_branch) = Git.Get_State(allow_unpushed_on_current: true, allow_uncommitted_changes: true);

        if (default_branch == current_branch)
        {
            throw new Log.Exception($"Cannot stage/unstage on default branch '{default_branch}'");
        }

        var globs = parsed_args.GetValue<string[]>("globs")!;

        Log.Step = globs.Length == 0 ? "Reset all changes" : $"Reset specified changes";
        Git.Run($"reset {(globs.Length == 0 ? "." : string.Join(' ', globs))}");
    }));

    app.Add(command);
}

void Add_Close_Action(RootCommand app)
{
    var command = new Command("close", "Collapse unpushed commits, push, checkout default, pull, and delete current branch");

    command.SetAction(parsed_args => Log.Capture(() =>
    {
        var current_text_color = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("Are you sure you want to push and delete this branch? [Y/N]: ");
        Console.ForegroundColor = current_text_color;

        if (Console.ReadLine() != "Y") return;

        var (default_branch, current_branch) = Git.Get_State(allow_unpushed_on_current: true);

        if (default_branch == current_branch)
        {
            throw new Log.Exception($"Cannot close default branch '{default_branch}'");
        }

        Log.Step = "Get unpushed commit messages";
        var unpushed_commit_log = Git.Run($"log --format=\"%B%x00\" {default_branch}..HEAD");

        Log.Step = "Parse and validate messages";

        var message_segments = new List<string>();

        var messages = unpushed_commit_log.Split("\0").Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

        if (messages.Length == 0) throw new Log.Exception("No unpushed commit messages found");

        foreach (var m in messages)
        {
            var lines = m.Split("\n");

            //Each message should have at least 4 lines: the title, the break, the date, and one or more bullets
            if (lines.Length < 4) throw new Log.Exception($"Found invalid unpushed commit message:\n{m}");

            //We don't care about the title, since we will retitle the new message
            lines = [.. lines.Skip(2).Where(l => !string.IsNullOrWhiteSpace(l))];

            //Validate the first line is a date (very very poorly)
            if (lines[0].StartsWith("::")) throw new Log.Exception($"Found invalid unpushed commit message:\n{m}");

            //Validate that all subsequent lines start with ":: " and are at least 10 chars long (no "wip" messages)
            if (lines.Skip(1).Any(l => !l.StartsWith(":: ") || l.Length < 10)) throw new Log.Exception($"Found invalid unpushed commit message:\n{m}");

            message_segments.Add(string.Join('\n', lines));
        }

        var new_message = string.Join("\n\n", new List<string> { current_branch }.Concat(message_segments))
            .Replace("\"", "\\\"");

        Log.Step = "Get last pushed commit hash";
        var last_pushed_commit_hash = Git.Run($"merge-base HEAD {default_branch}");

        Log.Step = "Strip unpushed commits";
        Git.Run($"reset --soft {last_pushed_commit_hash}");

        Log.Step = "Add new squashed commit";
        Git.Run($"commit -m \"{new_message}\"");

        Log.Step = "Push";
        Git.Run($"push -u origin {current_branch}");

        Log.Step = $"Switch to '{default_branch}'";
        Git.Run($"checkout {default_branch}");

        Log.Step = $"Merge 'origin/{default_branch}' into '{default_branch}' (--ff-only)";
        Git.Run($"merge --ff-only origin/{default_branch}");

        Log.Step = $"Delete '{current_branch}'";
        Git.Run($"branch -D {current_branch}");
    }));

    app.Add(command);
}

void Add_Commit_Action(RootCommand app)
{
    var command = new Command("commit", "Commit changes")
    {
        new Argument<string>("message") { Description = "Commit message. Should be a double-colon (::) separated list of points. Branch name and date will be prepended." }
    };

    command.SetAction(parsed_args => Log.Capture(() =>
    {
        var (default_branch, current_branch) = Git.Get_State(allow_unpushed_on_current: true, allow_uncommitted_changes: true);

        if (default_branch == current_branch)
        {
            throw new Log.Exception($"Cannot commit on default branch '{default_branch}'");
        }

        Log.Step = "Generate commit message";

        var message = parsed_args.GetValue<string>("message")!;

        var message_body = string.Join('\n', message.Split("::")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => ":: " + x.Trim()));

        var full_message = $"{current_branch}\n\n{DateTime.UtcNow}\n{message_body}"
            .Replace("\"", "\\\"");

        Log.Step = "Commit";
        Git.Run($"commit -m \"{full_message}\"");
    }));

    app.Add(command);
}

void Add_Config_Action(RootCommand app)
{
    var command = new Command("config", "Update tool config file");

    command.SetAction(parsed_args => Log.Capture(Config.Update_File));

    app.Add(command);
}