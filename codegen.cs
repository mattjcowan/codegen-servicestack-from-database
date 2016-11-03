//css_args /provider:%CSSCRIPT_DIR%\lib\CSSCodeProvider.v4.6.dll
//css_nuget Microsoft.CodeDom.Providers.DotNetCompilerPlatform
//css_nuget DatabaseSchemaReader
//css_nuget JsonPrettyPrinter
//css_nuget ServiceStack
//css_nuget ServiceStack.OrmLite
//css_nuget ServiceStack.Text
//css_nuget Microsoft.SqlServer.Types
//css_ref System.Data.Entity.Design.dll

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Entity.Design;
using System.Data.Entity.Design.PluralizationServices;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using DatabaseSchemaReader;
using DatabaseSchemaReader.DataSchema;

using JsonPrettyPrinterPlus;

using ServiceStack;
using ServiceStack.Text;

// sample usage: https://github.com/mjczone/sswc/blob/master/src/sswc/ProgramArgs.cs

[Description(@"A simple code generator for ServiceStack:

Usage:

  # run the code generator with the given config file
  cscs codegen.cs -config=myconfig.json
  
  # create a sample config file to get started
  cscs codegen.cs -create-config=myconfig.json
  
  # run the code generator
  cscs codegen.cs -connectionstring=mydb.sqlite -dialect=sqlite -namespace=MyDB -output=mydb.cs    
")]

public class ScriptConfig
{
    [Display(Name = "help", Description = "Get usage information", Order = 0), IgnoreDataMember]
    public bool Help { get; set; }

    [Display(Name = "config", Description = "Relative path to config file with advanced customization for the code generation process", Order = 1), IgnoreDataMember]
    public string Config { get; set; }

    [Display(Name = "create-config", Description = "Create a sample configuration file at the given relative path", Order = 1), IgnoreDataMember]
    public string CreateConfig { get; set; }

    [Display(Name = "connectionstring", Description = "Connection string to your database", Order = 2)]
    public string ConnectionString { get; set; }

    [Display(Name = "dialect", Description = "Dialect for your database (sqlite, sqlserver, sqlserver2012, oracle, pgsql, mysql)", Order = 3)]
    public string Dialect { get; set; }

    [Display(Name = "output", Description = "Directory or file to output code to. In case of a file, all the code will be output to that file (the directory in which the file is to be output must exist). In case of a directory, 3 files will be created: {Namespace}.Daos.cs, {Namespace}.ServiceTypes, and {Namespace}.Services (if the directory does not exist, it will be created)", Order = 4)]
    public string Output { get; set; }

    [Display(Name = "namespace", Description = "Root namespace for the generated code", Order = 5)]
    public string Namespace { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString) && string.IsNullOrWhiteSpace(Dialect))
        {
            throw new Exception("Provided configuration is missing a connection string and database dialect");
        }
        else if (!string.IsNullOrWhiteSpace(ConnectionString) && string.IsNullOrWhiteSpace(Dialect))
        {
            throw new Exception("Please specify a dialect for your connection string.");
        }
        else if (!string.IsNullOrWhiteSpace(Dialect) && string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new Exception("Please specify a connection string for your dialect.");
        }

        if (this.Dialect.IndexOf("sqlserver") >= 0)
        {
            this.SqlType = SqlType.SqlServer;
            if (this.Dialect.IndexOf("ce") >= 0)
            {
                this.SqlType = SqlType.SqlServerCe;
                this.SqlTypeVariant = "CE";
            }
            else if (this.Dialect.IndexOf("2005") >= 0)
                this.SqlTypeVariant = "2005";
            else if (this.Dialect.IndexOf("2008") >= 0)
                this.SqlTypeVariant = "2008";
            else if (this.Dialect.IndexOf("2012") >= 0)
                this.SqlTypeVariant = "2012";
            else if (this.Dialect.IndexOf("2014") >= 0)
                this.SqlTypeVariant = "2014";
            else if (this.Dialect.IndexOf("2016") >= 0)
                this.SqlTypeVariant = "2016";
        }
        else if (this.Dialect.IndexOf("mysql", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            this.SqlType = SqlType.MySql;
        }
        else if (this.Dialect.IndexOf("pgsql") >= 0 || this.Dialect.IndexOf("postgresql") >= 0)
        {
            this.SqlType = SqlType.PostgreSql;
        }
        else if (this.Dialect.IndexOf("oracle") >= 0)
        {
            this.SqlType = SqlType.Oracle;
        }
        else if (this.Dialect.IndexOf("sqlite") >= 0)
        {
            this.SqlType = SqlType.SQLite;
        }
        else
            throw new ArgumentException(nameof(this.Dialect));
    }

    // properties that cannot be configured via arguments at this time (ONLY within Config file)
    public SqlType SqlType { get; set; }
    public string SqlTypeVariant { get; set; }

    public string DaoNamespace { get; set; } = "Daos";
    public string ServiceTypeNamespace { get; set; } = "ServiceTypes";
    public string ServiceNamespace { get; set; } = "Services";

    public bool IncludeForeignKeyDeleteAndUpdateRules { get; set; } = true;
    public bool OmitSchemaAnnotationIfOnlyOneSchemaExists { get; set; } = true;

    public bool GenerateDaos { get; set; } = true;
    public bool GenerateServiceTypes { get; set; } = true;
    public bool GenerateServices { get; set; } = true;

    public string DaosFile { get; set; } = "Daos.cs";
    public string ServiceTypesFile { get; set; } = "ServiceTypes.cs";
    public string ServicesFile { get; set; } = "Services.cs";

    public List<string> ExtraUsingNamespacesForDaos { get; set; } = new List<string> { };
    public List<string> ExtraUsingNamespacesForServiceTypes { get; set; } = new List<string>() { };
    public List<string> ExtraUsingNamespacesForServices { get; set; } = new List<string>() { };

    public List<string> ExcludeSchemas { get; set; } = new List<string>();
    public Dictionary<string, List<string>> ExcludeSchemaTables { get; set; } = new Dictionary<string, List<string>>() {
        { "dbo", new List<string>() }
    };
    public Dictionary<string, Dictionary<string, List<string>>> ExcludeSchemaTableColumns { get; set; } = new Dictionary<string, Dictionary<string, List<string>>>() {
                            { "dbo", new Dictionary<string, List<string>>() {
                                { "Employees", new List<string>() }
                            }}
    };

    public List<string> IncludeSchemas { get; set; } = new List<string>();
    public Dictionary<string, List<string>> IncludeSchemaTables { get; set; } = new Dictionary<string, List<string>>() {
        { "dbo", new List<string>() }
    };
    public Dictionary<string, Dictionary<string, List<string>>> IncludeSchemaTableColumns { get; set; } = new Dictionary<string, Dictionary<string, List<string>>>() {
                            { "dbo", new Dictionary<string, List<string>>() {
                                { "Employees", new List<string>() }
                            }}
    };

    public Dictionary<string, string> TableNameToClassNameOverrides { get; set; } = new Dictionary<string, string>{
        { "SalesEmployee", "SalesPerson" }
    };
    public Dictionary<string, string> TableNameToCollectionClassNameOverrides { get; set; } = new Dictionary<string, string> {
        { "SalesPerson", "SalesPeople" }
    };
    public Dictionary<string, Dictionary<string, string>> ColumnNameToPropertyNameOverrides { get; set; } = new Dictionary<string, Dictionary<string, string>> {
            { "SomeTable", new Dictionary<string, string> {
                { "ColumnA", "PropA" }
            }}
        };
}

class Script
{
    static public void Main(string[] args)
    {
        if (args == null || args.Length == 0)
            return;

        ScriptConfig scriptConfig;

        try
        {
            scriptConfig = args.As<ScriptConfig>();

            if (scriptConfig.Help)
            {
                Console.WriteLine(ConsoleArgs.HelpFor<ScriptConfig>(true));
                return;
            }

            if (!string.IsNullOrWhiteSpace(scriptConfig.Config))
            {
                if (!File.Exists(scriptConfig.Config))
                    throw new System.IO.FileNotFoundException("Could not locate file: " + scriptConfig.Config, scriptConfig.Config);

                var config = File.ReadAllText(scriptConfig.Config).FromJson<ScriptConfig>();
                scriptConfig.PopulateWithNonDefaultValues(config);
            }

            if (!string.IsNullOrWhiteSpace(scriptConfig.CreateConfig))
            {
                JsConfig.IncludeNullValues = true;
                JsConfig.IncludeNullValuesInDictionaries = true;
                JsConfig.EmitCamelCaseNames = true;
                JsConfig.DateHandler = DateHandler.ISO8601;
                JsConfig.TreatEnumAsInteger = false;
                if (string.IsNullOrWhiteSpace(scriptConfig.ConnectionString))
                    scriptConfig.ConnectionString = @"Data Source=.\sqlexpress;Initial Catalog=MyAppDB;Integrated Security=True";
                if (string.IsNullOrWhiteSpace(scriptConfig.Dialect))
                    scriptConfig.Dialect = "SqlServer2012";
                File.WriteAllText(scriptConfig.CreateConfig, scriptConfig.ToJson().PrettyPrintJson());
                return;
            }

            scriptConfig.Validate();
        }
        catch (ArgumentException) // this will catch issues with arguments
        {
            Console.WriteLine(ConsoleArgs.HelpFor<ScriptConfig>(false));
            return;
        }
        catch (Exception ex) // catch any other errors here
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(ex.Message);
#if DEBUG
            Console.WriteLine(ex);
#endif			
            Console.ResetColor();
            return;
        }

        var script = new Script();

        using (var reader = new DatabaseReader(scriptConfig.ConnectionString, scriptConfig.SqlType))
        {
            var options = new CodeGenOptions();
            options.PopulateWith(scriptConfig);
            script.PrintDaos(reader, options);
        }
    }

    public class CodeGenOptions : ScriptConfig
    {
        // public string Namespace { get; set; } = "CodeGenerated";
        // public string DaoNamespace { get; set; } = "Daos";
        // public string ServiceTypeNamespace { get; set; } = "ServiceTypes";
        // public string ServiceNamespace { get; set; } = "Services";

        // public bool IncludeForeignKeyDeleteAndUpdateRules { get; set; } = true;
        // public bool OmitSchemaAnnotationIfOnlyOneSchemaExists { get; set; } = true;

        // public bool GenerateDaos { get; set; } = true;
        // public bool GenerateServiceTypes { get; set; } = true;
        // public bool GenerateServices { get; set; } = true;

        // public bool DaosFile { get; set; } = "Daos.cs";
        // public bool ServiceTypesFile { get; set; } = "Daos.cs";
        // public bool ServicesFile { get; set; } = "Daos.cs";

        // public List<string> ExtraUsingNamespacesForDaos { get; set; } = new List<string> { };
        // public List<string> ExtraUsingNamespacesForServiceTypes { get; set; } = new List<string>() { };
        // public List<string> ExtraUsingNamespacesForServices { get; set; } = new List<string>() { };

        public Func<string, string> ForeignKeyPropertyToForeignKeyReferenceName { get; set; } = fkPropertyName =>
            fkPropertyName.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ? fkPropertyName.Substring(0, fkPropertyName.Length - 2) : fkPropertyName + "_";

        public Func<string, string, string, bool> IncludeForeignKeyReference { get; set; } = (schema, table, column) => true;
        public Func<string, string, string, string, bool> IncludeCollectionReference { get; set; } = (schema, table, pkTable, pkColumn) => true;

        public Func<string, string, string> TableNameToClassNameConverter { get; set; } = (schema, table) =>
        {
            var nm = table.Replace(" ", "");
            return pluralizer.IsPlural(nm) ? pluralizer.Singularize(nm) : nm;
        };

        public Func<string, string, string, string> ColumnNameToPropertyNameConverter { get; set; } = (schema, table, column) =>
        {
            var nm = column.Replace(" ", "");

            if (CSharpKeywords.Contains(nm.ToLower()) || nm.Equals(table, StringComparison.OrdinalIgnoreCase))
                nm = "_" + nm;
            if (nm.EndsWith("ID"))
                nm = nm.Substring(0, nm.Length - 1) + "d";
            return nm;
        };

        public Func<string, string, string> TableNameToListNameConverter { get; set; } = (schema, table) =>
        {
            var nm = table.Replace(" ", "");
            return pluralizer.IsPlural(nm) ? nm : pluralizer.Pluralize(nm);
        };

        public Func<string, bool> ExcludeSchemaFilter { get; set; } = schema => false;
        public Func<string, string, bool> ExcludeTableFilter { get; set; } = (schema, table) => false;
    }

    // tableName, <DatabaseTable, schemaName, className, collectionName>
    Dictionary<string, Tuple<DatabaseTable, string, string, string>> tableMap = new Dictionary<string, Tuple<DatabaseTable, string, string, string>>();

    void PrintDaos(DatabaseReader reader, CodeGenOptions opt)
    {
        wl("// ----------------------------------------------------------------------");
        wl("// Auto-generated on " + DateTime.Now);
        wl("// ");
        wl("// Use partial classes to override or extend functionality, thank you!!");
        wl("// ----------------------------------------------------------------------");
        wl("");

        var database = GetDatabase(reader);

        // print DAOs
        foreach (var usingStatement in DefaultUsingNamespaces)
        {
            if (!opt.ExtraUsingNamespacesForDaos.Contains(usingStatement))
                opt.ExtraUsingNamespacesForDaos.Add(usingStatement);
        }
        foreach (var usingStatement in opt.ExtraUsingNamespacesForDaos.OrderByDescending(u => u.StartsWith("System")).ThenBy(u => u))
        {
            wl("using {0};", usingStatement);
        }
        wl("");
        wl("namespace {0}", opt.Namespace + "." + opt.DaoNamespace);
        wl("{");
        wli("");

        var groupedTables = database.Tables.Where(t => !opt.ExcludeSchemaFilter(t.SchemaOwner)).GroupBy(t => t.SchemaOwner);

        if (groupedTables.Count() < 2 && opt.OmitSchemaAnnotationIfOnlyOneSchemaExists)
            OmitSchemaAnnotation = true;
        foreach (var tables in groupedTables)
        {
            var schemaName = tables.Key;

            if (opt.ExcludeSchemaFilter(schemaName))
                continue;

            foreach (var table in tables)
            {
                var tableName = table.Name;

                if (opt.ExcludeTableFilter(schemaName, tableName))
                    continue;

                var className = opt.TableNameToClassNameConverter(schemaName, tableName);
                var collectionName = opt.TableNameToListNameConverter(schemaName, tableName);
                tableMap.Add(tableName, new Tuple<DatabaseTable, string, string, string>(table, schemaName, className, collectionName));
                if (!OmitSchemaAnnotation) wl("[Schema(\"{0}\")]", schemaName);
                wl("[Alias(\"{0}\")]", tableName);
                foreach (var idx in table.Indexes.Where(i => i.IsUnique == false && i.Columns.Count > 1))
                {
                    wl("[CompositeIndex(\"{0}\", Name = \"{1}\", Unique = false)]",
                        string.Join("\", \"", idx.Columns.Select(c => opt.ColumnNameToPropertyNameConverter(schemaName, tableName, c.Name))),
                        idx.Name);
                }
                foreach (var idx in table.UniqueKeys.Where(i => i.Columns.Count > 1))
                {
                    wl("[CompositeIndex(\"{0}\", Name = \"{1}\", Unique = true)]",
                        string.Join("\", \"", idx.Columns.Select(c => opt.ColumnNameToPropertyNameConverter(schemaName, tableName, c))),
                        idx.Name);
                }
                if (table.PrimaryKey.Columns.Count > 1)
                {
                    wl("[CompositeKey(\"{0}\")]",
                        string.Join("\", \"", table.PrimaryKey.Columns.Select(c => opt.ColumnNameToPropertyNameConverter(schemaName, tableName, c))));
                }
                wl("public partial class {0}", className);
                wl("{");
                wli("#region Properties");
                wl("");
                foreach (var column in table.Columns.OrderByDescending(c => c.IsPrimaryKey).ThenBy(c => c.Ordinal))
                {
                    try
                    {
                        var propertyName = opt.ColumnNameToPropertyNameConverter(schemaName, tableName, column.Name);
                        var columnType = column.DataType;
                        var columnCSharpType = column.DataType?.NetDataTypeCSharpName?.Replace("System.", "");

                        if (columnType == null)
                        {
                            switch (column.DbDataType.ToLower())
                            {
                                case "hierarchyid":
                                    columnCSharpType = typeof(Microsoft.SqlServer.Types.SqlHierarchyId).FullName;
                                    break;
                                case "geography":
                                    columnCSharpType = typeof(Microsoft.SqlServer.Types.SqlGeography).FullName;
                                    break;
                                case "geometry":
                                    columnCSharpType = typeof(Microsoft.SqlServer.Types.SqlGeometry).FullName;
                                    break;
                                default:
                                    columnCSharpType = "object /* DbDataType: " + column.DbDataType + " */";
                                    break;
                            }
                        }

                        wl("[Alias(\"{0}\")]", column.Name);
                        if (column.IsPrimaryKey && table.PrimaryKey.Columns.Count == 1)
                        {
                            wl("[PrimaryKey]");
                            propertyName = "Id";
                        }
                        else if (!column.Nullable)
                        {
                            wl("[Required]");
                        }
                        if (column.IsUniqueKey && table.UniqueKeys.Where(b => b.Columns.Any(c => c == column.Name)).Count() == 1)
                        {
                            wl("[Index(true)]");
                        }

                        var primaryKeyClassName = string.Empty;

                        if (!column.IsPrimaryKey && column.IsForeignKey)
                        {
                            var fk = table.ForeignKeys.First(f => f.Columns.Any(c => c == column.Name));
                            primaryKeyClassName = opt.TableNameToClassNameConverter(schemaName, column.ForeignKeyTable.Name);
                            if (opt.IncludeForeignKeyDeleteAndUpdateRules)
                                wl("[ForeignKey(typeof({0}), ForeignKeyName = \"{1}\", OnDelete = \"{2}\", OnUpdate = \"{3}\")]", primaryKeyClassName, fk.Name, fk.DeleteRule, fk.UpdateRule);
                            else
                                wl("[ForeignKey(typeof({0}), ForeignKeyName = \"{1}\"]", primaryKeyClassName, fk.Name);
                        }
                        if (column.IsAutoNumber)
                            wl("[AutoIncrement]");
                        if (columnCSharpType.Equals("string", StringComparison.OrdinalIgnoreCase))
                            wl("[StringLength({0})]", column.Length);

                        wl("public virtual {0}{2} {1} {{ get; set; }}", columnCSharpType, propertyName, column.Nullable && IsNullableType(columnCSharpType) ? "?" : "");

                        if (!string.IsNullOrWhiteSpace(primaryKeyClassName) && opt.IncludeForeignKeyReference(schemaName, tableName, column.Name))
                        {
                            var relationName = opt.ForeignKeyPropertyToForeignKeyReferenceName(propertyName);
                            wl("[Reference]");
                            wl("public virtual {0} {1} {{ get; set; }}", primaryKeyClassName, relationName);
                        }
                    }
                    catch (Exception ex)
                    {
                        wl("//ERR: unable to process column {0}: {1}", column.Name, ex);
                    }
                }

                wl("");

                if (table.ForeignKeyChildren.Count > 0)
                {
                    foreach (var fkc in table.ForeignKeyChildren)
                    {
                        var collectionClassName = opt.TableNameToListNameConverter(fkc.SchemaOwner, fkc.Name);

                        if (!string.IsNullOrWhiteSpace(collectionClassName))
                        {
                            if (opt.IncludeCollectionReference(schemaName, tableName, fkc.SchemaOwner, fkc.Name))
                            {
                                wl("[Reference]");
                                wl("public virtual List<{0}> {1} {{ get; set; }}", opt.TableNameToClassNameConverter(fkc.SchemaOwner, fkc.Name), collectionClassName);
                            }
                        }
                    }
                }

                wl("");
                wl("#endregion //Properties");

                wlu(null);
                wl("}");
                wl("");
            }
        }
        wlu("");
        wl("}");
    }

    // DO NOT MODIFY BELOW THIS LINE, THESE ARE PRIVATE METHOD HELPERS
    static PluralizationService pluralizer = PluralizationService.CreateService(System.Globalization.CultureInfo.CurrentUICulture);
    static bool OmitSchemaAnnotation = false;
    static int l = 0;
    static readonly StringBuilder sb = new StringBuilder();
    /// write line
    void wl(string fmt, params object[] args)
    {
        if (fmt != null && args != null && args.Length > 0)
            Console.WriteLine(new String(' ', l).ToString() + string.Format(fmt, args));
        else if (fmt != null)
            Console.WriteLine(new String(' ', l).ToString() + fmt);
    }
    /// write line indented
    void wli(string fmt, params object[] args)
    {
        l += 4;
        wl(fmt, args);
    }
    /// write line unindented
    void wlu(string fmt = null, params object[] args)
    {
        l -= 4;
        wl(fmt, args);
    }

    DatabaseSchema GetDatabase(DatabaseReader reader)
    {
        var debugListeners = Debug.Listeners.Cast<TraceListener>().ToArray();
        Debug.Listeners.Clear();
        var schema = reader.ReadAll();
        Debug.Listeners.AddRange(debugListeners);
        return schema;
    }
    static readonly string[] CSharpKeywords = new[] {
        "abstract", "add", "as", "ascending", "async", "await", "base", "bool", "break", "by", "byte", "case",
        "catch", "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "descending",
        "do", "double", "dynamic", "else", "enum", "equals", "explicit", "extern", "false", "finally", "fixed",
        "float", "for", "foreach", "from", "get", "global  goto", "group", "if", "implicit", "in", "int", "interface",
        "internal", "into", "is", "join", "let", "lock", "long", "namespace", "new", "null", "object", "on", "operator",
        "orderby", "out", "override", "params", "partial", "private", "protected", "public", "readonly", "ref",
        "remove", "return", "sbyte", "sealed", "select", "set", "short", "sizeof", "stackalloc", "static", "string",
        "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
        "using", "value", "var", "virtual", "void", "volatile", "where", "while", "yield" };
    static readonly string[] DefaultUsingNamespaces = new[] {
        "System", "System.Collections.Generic", "ServiceStack.DataAnnotations"
    };

    bool IsNullableType(string csharpTypeName)
    {
        switch (csharpTypeName.ToLower())
        {
            case "string":
            case "byte[]":
                return false;
            case "int":
            case "long":
            case "datetime":
            case "guid":
            case "bool":
                return true;
            default:
                break;
        }
        return !csharpTypeName.Contains("Microsoft.SqlServer.Types.");
    }
}

// Adapted from: MadProps.AppArgs
internal static class ConsoleArgs
{
    private static readonly Regex Pattern = new Regex("[/-](?'key'[^\\s=:]+)"
        + "([=:]("
            + "((?'open'\").+(?'value-open'\"))"
            + "|"
            + "(?'value'.+)"
        + "))?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UriPattern = new Regex(@"[\\?&](?'key'[^&=]+)(=(?'value'[^&]+))?", RegexOptions.Compiled);
    private static readonly Regex QueryStringPattern = new Regex(@"(^|&)(?'key'[^&=]+)(=(?'value'[^&]+))?", RegexOptions.Compiled);

    private static IEnumerable<ArgProperty> PropertiesOf<T>()
    {
        return from p in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.SetProperty).Where(p => p.Attribute<DisplayAttribute>() != null)
               let d = p.Attribute<DescriptionAttribute>()
               let alias = p.Attribute<DisplayAttribute>()
               let def = p.Attribute<DefaultValueAttribute>()
               select new ArgProperty
               {
                   Property = p,
                   Name = string.IsNullOrWhiteSpace(alias?.GetShortName()) ? p.Name.ToLower() : alias.GetShortName(),
                   Type = p.PropertyType,
                   Order = alias == null ? 0 : alias.GetOrder().GetValueOrDefault(0),
                   Required = p.Attribute<RequiredAttribute>() != null,
                   RequiresValue = !(p.PropertyType == typeof(bool) || p.PropertyType == typeof(bool?)),
                   Description = ((!string.IsNullOrWhiteSpace(d?.Description) ? d.Description : (alias != null ? alias.GetDescription() : string.Empty)) +
                                  (def?.Value == null ? string.Empty : $" [default: {def.Value}]")).Trim()
               };
    }

    /// <summary>
    /// Parses the arguments in <paramref name="args"/> and creates an instance of <typeparamref name="T"/> with the
    /// corresponding properties populated.
    /// </summary>
    /// <typeparam name="T">The custom type to be populated from <paramref name="args"/>.</typeparam>
    /// <param name="args">Command-line arguments, usually in the form of "/name=value".</param>
    /// <returns>A new instance of <typeparamref name="T"/>.</returns>
    public static T As<T>(this string[] args) where T : class, new()
    {
        var arguments = (from a in args
                         let match = Pattern.Match(a)
                         where match.Success
                         select new
                         {
                             Key = match.Groups["key"].Value.ToLower(),
                             match.Groups["value"].Value
                         }
            ).ToDictionary(a => a.Key, a => a.Value);

        return arguments.As<T>();
    }

    /// <summary>
    /// Parses the arguments in the supplied string and creates an instance of <typeparamref name="T"/> with the
    /// corresponding properties populated.
    /// The string should be in the format "key1=value1&amp;key2=value2&amp;key3=value3".
    /// </summary>
    /// <typeparam name="T">The custom type to be populated from <paramref name="queryString"/>.</typeparam>
    /// <param name="queryString">Command-line arguments, usually in the form of "/name=value".</param>
    /// <returns>A new instance of <typeparamref name="T"/>.</returns>
    public static T As<T>(this string queryString) where T : new()
    {
        var arguments = (from match in QueryStringPattern.Matches(queryString).Cast<Match>()
                         where match.Success
                         select new
                         {
                             Key = match.Groups["key"].Value.ToLower(),
                             match.Groups["value"].Value
                         }
            ).ToDictionary(a => a.Key, a => a.Value);

        return arguments.As<T>();
    }

    /// <summary>
    /// Parses the URI parameters in <paramref name="uri"/> and creates an instance of <typeparamref name="T"/> with the
    /// corresponding properties populated.
    /// </summary>
    /// <typeparam name="T">The custom type to be populated from <paramref name="uri"/>.</typeparam>
    /// <param name="uri">A URI, usually a ClickOnce activation URI.</param>
    /// <returns>A new instance of <typeparamref name="T"/>.</returns>
    public static T As<T>(this Uri uri) where T : new()
    {
        var arguments = (from match in UriPattern.Matches(uri.ToString()).Cast<Match>()
                         where match.Success
                         select new
                         {
                             Key = match.Groups["key"].Value.ToLower(),
                             match.Groups["value"].Value
                         }
            ).ToDictionary(a => a.Key, a => a.Value);

        return arguments.As<T>();
    }

    /// <summary>
    /// Parses the name/value pairs in <paramref name="arguments"/> and creates an instance of <typeparamref name="T"/> with the
    /// corresponding properties populated.
    /// </summary>
    /// <typeparam name="T">The custom type to be populated from <paramref name="arguments"/>.</typeparam>
    /// <param name="arguments">The key/value pairs to be parsed.</param>
    /// <returns>A new instance of <typeparamref name="T"/>.</returns>
    public static T As<T>(this Dictionary<string, string> arguments) where T : new()
    {
        T result = new T();

        var props = PropertiesOf<T>().ToList();

        foreach (var arg in arguments)
        {
            var matches = props.Where(p => p.Name.StartsWith(arg.Key, StringComparison.OrdinalIgnoreCase)).ToList();

            if (matches.Count == 0)
            {
                //Ignore unknown arguments
                //throw new ArgumentException("Unknown argument '" + arg.Key + "'");
                continue;
            }
            else if (matches.Count > 1)
            {
                throw new ArgumentException("Ambiguous argument '" + arg.Key + "'");
            }

            var prop = matches[0];

            if (!String.IsNullOrWhiteSpace(arg.Value))
            {
                if (prop.Type.IsArray)
                {
                    string v = arg.Value;

                    if (v.StartsWith("{") && v.EndsWith("}"))
                    {
                        v = v.Substring(1, arg.Value.Length - 2);
                    }

                    var values = v.Split(',').ToArray();
                    var array = Array.CreateInstance(prop.Type.GetElementType(), values.Length);

                    for (int i = 0; i < values.Length; i++)
                    {
                        var converter = TypeDescriptor.GetConverter(prop.Type.GetElementType());
                        array.SetValue(converter.ConvertFrom(values[i]), i);
                    }

                    prop.Property.SetValue(result, array, null);
                }
                else
                {
                    var converter = TypeDescriptor.GetConverter(prop.Type);
                    prop.Property.SetValue(result, converter.ConvertFromString(arg.Value), null);
                }
            }
            else if (prop.Type == typeof(bool))
            {
                prop.Property.SetValue(result, true, null);
            }
            else
            {
                throw new ArgumentException("No value supplied for argument '" + arg.Key + "'");
            }
        }

        foreach (var p in props.Where(p => p.Required))
        {
            if (!arguments.Keys.Any(a => p.Name.StartsWith(a)))
            {
                throw new ArgumentException("Argument missing: '" + p.Name + "'");
            }
        }

        return result;
    }

    /// <summary>
    /// Returns a string describing the arguments necessary to populate an instance of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">A class representing the potential application arguments.</typeparam>
    /// <returns>A string describing the arguments necessary to populate an instance of <typeparamref name="T"/></returns>
    public static string HelpFor<T>(bool includeUsageExamples = false)
    {
        var props = PropertiesOf<T>().Where(p => !p.Name.StartsWith("_")).OrderBy(p => p.RequiresValue).ThenBy(p => p.Name).ToList();

        var sb = new StringBuilder();

        //sb.Append(Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]));
        sb.Append(Path.GetFileName(new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath).ToLower());
        foreach (var p in props.Where(p => p.Required))
        {
            sb.Append(" /" + p.Name + (p.RequiresValue ? "=value" : ""));
        }

        foreach (var p in props.Where(p => !p.Required))
        {
            sb.Append(" [/" + p.Name + (p.RequiresValue ? "=value" : "") + "]");
        }

        sb.AppendLine();
        sb.AppendLine();

        if (includeUsageExamples)
        {
            var usage = typeof(T).Attribute<DescriptionAttribute>()?.Description;

            if (!string.IsNullOrWhiteSpace(usage))
            {
                sb.Append(usage.Trim());
                sb.AppendLine();
                sb.AppendLine();
            }
        }

        var hasRequiredArguments = false;
        var output = props.Where(p => p.Required).OrderBy(p => p.Order).ToDictionary(
            k => "/" + k.Name.Trim(),
            v => (v.Description ?? "").Trim());

        if (output.Count > 0)
        {
            hasRequiredArguments = true;
            sb.AppendLine("Required arguments:");
            sb.AppendLine();
            output.To2ColumnsOfText(s => sb.Append(s), 0, 0, 2, 1, 1, 2);
        }

        output = props.Where(p => !p.Required).OrderBy(p => p.Order).ToDictionary(
            k => "/" + k.Name.Trim(),
            v => (v.Description ?? "").Trim());
        if (output.Count > 0)
        {
            if (hasRequiredArguments) sb.AppendLine();
            sb.AppendLine("Optional arguments:");
            sb.AppendLine();
            output.To2ColumnsOfText(s => sb.Append(s), 0, 0, 2, 1, 1, 2);
        }

        return sb.ToString();
    }

    private class ArgProperty
    {
        public PropertyInfo Property { get; set; }
        public string Name { get; set; }
        public bool Required { get; set; }
        public bool RequiresValue { get; set; }
        public Type Type { get; set; }
        public string Description { get; set; }
        public int Order { get; set; }
    }
}

public static class PropertyInfoExtensions
{
    public static T Attribute<T>(this PropertyInfo p)
    {
        return p.GetCustomAttributes(typeof(T), true).Cast<T>().FirstOrDefault();
    }

    public static T Attribute<T>(this Type t)
    {
        return t.GetCustomAttributes(typeof(T), true).Cast<T>().FirstOrDefault();
    }

    public static T Attribute<T>(this Assembly ass) where T : Attribute
    {
        return ass.GetCustomAttributes(typeof(T), false).Cast<T>().FirstOrDefault();
    }

    public static void To2ColumnsOfText(this Dictionary<string, string> target, Action<string> output, int col1Size = 0, int col2Size = 0, int col1LeftMargin = 0, int col1RightMargin = 0, int col2LeftMargin = 0, int col2RightMargin = 0)
    {
        var cWidth = 80;

        if (col1Size == 0 || col2Size == 0)
        {
            try
            {
                cWidth = Console.WindowWidth;
            }
            catch
            {
                // ignored
            }
        }

        if (col1Size == 0 && col2Size == 0)
            col1Size = target.Keys.Max(k => k.Length + col1LeftMargin + col1RightMargin + 1);
        if (col2Size == 0 && col1Size > cWidth)
            col1Size = cWidth / 2;

        if (col1Size == 0 && col2Size > 0)
            col1Size = cWidth - col2Size;
        if (col2Size == 0 && col1Size > 0)
            col2Size = cWidth - col1Size;

        foreach (var kvp in target)
        {
            var col1Lines = WordWrapToLines(kvp.Key, col1Size, col1LeftMargin, col1RightMargin);
            var col2Lines = WordWrapToLines(kvp.Value, col2Size, col2LeftMargin, col2RightMargin);

            var len = Math.Max(col1Lines.Count, col2Lines.Count);

            while (col1Lines.Count < col2Lines.Count)
                col1Lines.Add("");
            while (col2Lines.Count < col1Lines.Count)
                col2Lines.Add("");

            for (var i = 0; i < len; i++)
            {
                output(col1Lines[i].PadRight(col1Size));
                output(col2Lines[i]);
                output(Environment.NewLine);
            }
        }
    }

    public static void WordWrapToFunc(this string text, Action<string> output, int maxWidth, int leftMargin, int rightMargin)
    {
        while (text.Contains("  "))
            text = text.Replace("  ", " ");

        var width = maxWidth - leftMargin - rightMargin;

        var words = text.Split(' ');
        var currentLine = new StringBuilder();

        for (var w = 0; w < words.Length; w++)
        {
            var word = words[w];

            if ((currentLine.Length + word.Length) < width)
            {
                currentLine.Append(w == 0 ? word : " " + word);
            }
            else
            {
                var line = currentLine.ToString().Trim();
                output(line.PadLeft(line.Length + leftMargin));
                currentLine.Clear();
                currentLine.Append(word);
            }
        }
        if (currentLine.Length > 0)
        {
            var line = currentLine.ToString().Trim();
            output(line.PadLeft(line.Length + leftMargin));
        }
    }

    public static void WordWrapToWriter(this string text, TextWriter output, int maxWidth, int leftMargin, int rightMargin)
    {
        WordWrapToFunc(text, output.WriteLine, maxWidth, leftMargin, rightMargin);
    }

    public static List<string> WordWrapToLines(this string text, int maxWidth, int leftMargin, int rightMargin)
    {
        var lines = new List<string>();
        WordWrapToFunc(text, s => lines.Add(s), maxWidth, leftMargin, rightMargin);
        return lines;
    }

    public static string WordWrapToString(this string text, int maxWidth, int leftMargin, int rightMargin)
    {
        var sb = new StringBuilder();
        WordWrapToFunc(text, s => sb.AppendLine(s), maxWidth, leftMargin, rightMargin);
        return sb.ToString().TrimEnd();
    }
}