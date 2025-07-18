static class Log
{
    private static int Current_Step_Index = 0;
    private static string? Current_Step_Description = null;

    private static void End_Step(bool is_success, string? reason = null)
    {
        if (is_success)
        {
            if (Current_Step_Description is not null) Console.WriteLine();
        }
        else
        {
            var current_text_color = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            if (Current_Step_Description is not null)
            {
                var base_message = $"Step {Current_Step_Index}: {Current_Step_Description}";
                Console.Write(new string('\b', base_message.Length));
                Console.WriteLine(base_message + " [FAILED]");
            }
            if (reason is not null) Console.WriteLine(reason);
            Console.ForegroundColor = current_text_color;
        }

        Current_Step_Index++;
    }

    public static int Capture(Action action)
    {
        try
        {
            action();
            End_Step(is_success: true);
            return 0;
        }
        catch (Exception e)
        {
            End_Step(is_success: false, reason: e.Message);
            return 1;
        }
        catch (System.Exception)
        {
            End_Step(is_success: false);
            throw;
        }
    }

    public static string Step
    {
        set
        {
            if (Current_Step_Description is not null) End_Step(is_success: true);
            Current_Step_Description = value;
            var current_text_color = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Blue;
            //Only Write, not WriteLine because End_Step may want to override this text
            //if the step fails. It will add the newline if the step succeeds.
            Console.Write($"Step {Current_Step_Index}: {Current_Step_Description}");
            Console.ForegroundColor = current_text_color;
        }
    }

    public class Exception(string message) : System.Exception(message);
}