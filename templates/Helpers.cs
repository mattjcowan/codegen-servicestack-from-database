using System;

using HandlebarsDotNet;

namespace HandlebarsUtils
{
    public class Helpers
    {
        public static void Init()
        {
            // Example: {{now "MM-dd-yyyy"}}
            Handlebars.RegisterHelper("now", (writer, context, parameters) =>
            {
                var dtFormat = parameters.Length > 0 && parameters[0] is string ? (string) parameters[0] : "u";
                writer.WriteSafeString(DateTime.Now.ToString(dtFormat));
            });
        }
    }
}