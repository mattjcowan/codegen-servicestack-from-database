//css_args /provider:%CSSCRIPT_DIR%\lib\CSSCodeProvider.v4.6.dll
//css_nuget Microsoft.CodeDom.Providers.DotNetCompilerPlatform
//css_nuget DatabaseSchemaReader
//css_nuget JsonPrettyPrinter
//css_nuget ServiceStack
//css_nuget ServiceStack.OrmLite
//css_nuget ServiceStack.Text
//css_nuget Microsoft.SqlServer.Types
//css_ref System.Data.Entity.Design.dll
//css_import templates\helpers.cs;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Entity.Design;
using System.Data.Entity.Design.PluralizationServices;
using System.Diagnostics;
using System.Globalization;
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

class Script
{
    public static void Main(string[] args)
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
            Console.WriteLine(ex);
            Console.ResetColor();
            return;
        }

        var codeGenConfig = ConvertScriptConfigToCodeGenConfig(scriptConfig);
        Run(codeGenConfig);
    }

    private static void Run(ICodeGenConfig config)
    {
        new CodeGenerator(config).Execute();
    }

    private static ICodeGenConfig ConvertScriptConfigToCodeGenConfig(ScriptConfig scriptConfig)
    {
        var config = new CodeGenConfig().PopulateWith(scriptConfig);

        return config;
    }
}

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

        if (!this.Dialect.Contains("sqlserver") &&
            !this.Dialect.Contains("oracle") &&
            !(this.Dialect.Contains("pgsql") || this.Dialect.Contains("postgresql")) &&
            !this.Dialect.Contains("mysql") &&
            this.Dialect.Contains("sqlite"))
        {
            throw new Exception("Invalid dialect! Options are: sqlserver, sqlserver{variant}, oracle, pgsql or postgresql, mysql, and sqlite");
        }
    }

    // properties that cannot be configured via arguments at this time (ONLY within Config file)

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
    public Dictionary<string, Dictionary<string, string>> TableColumnNameToPropertyNameOverrides { get; set; } = new Dictionary<string, Dictionary<string, string>> {
            { "SomeTable", new Dictionary<string, string> {
                { "ColumnA", "PropA" }
            }}
        };
}

public interface ICodeGenConfig
{
    string OutputDir { get; }
    string ConnectionString { get; }
    string Dialect { get; }
    string Namespace { get; }
    string DaosNamespace { get; }
    string ServiceTypesNamespace { get; }
    string ServicesNamespace { get; }
    bool GenerateDaos { get; }
    bool GenerateServiceTypes { get; }
    bool GenerateServices { get; }
    string DaosFile { get; }
    string ServiceTypesFile { get; }
    string ServicesFile { get; }
    bool IncludeForeignKeyDeleteAndUpdateRules { get; }
    bool OmitSchemaAnnotationIfOnlyOneSchemaExists { get; }
    List<string> ExtraUsingNamespacesForDaos { get; }
    List<string> ExtraUsingNamespacesForServiceTypes { get; }
    List<string> ExtraUsingNamespacesForServices { get; }
    Func<string, bool> ExcludeSchemaFilter { get; }
    Func<string, string, bool> ExcludeTableFilter { get; }
    Func<string, string, string, bool> ExcludeTableColumnFilter { get; }
    Func<string, string, string> TableNameToClassNameConverter { get; }
    Func<string, string, string> TableNameToCollectionNameConverter { get; }
    Func<string, string, string, string> TableColumnNameToPropertyNameConverter { get; }
    Func<string, string> ForeignKeyPropertyToForeignKeyReferenceName { get; }
    Func<string, string, string, bool> IncludeForeignKeyReference { get; }
    Func<string, string, string, string, bool> IncludeCollectionReference { get; }
    Func<string> InsertCodeAfterUsingStatementsForDaos { get; }
    Func<string> InsertCodeAfterUsingStatementsForServiceTypes { get; }
    Func<string> InsertCodeAfterUsingStatementsForServices { get; }
    Func<string, string> InsertCodeBeforeClassDeclarationForDaos { get; }
    Func<string, string> InsertCodeBeforeClassDeclarationForServiceTypes { get; }
    Func<string, string> InsertCodeBeforeClassDeclarationForServices { get; }
    Func<string, string> InsertCodeInsideInterfaceDeclarationsForDaos { get; }
    Func<string, string> InsertCodeInsideInterfaceDeclarationsForServiceTypes { get; }
    Func<string, string> InsertCodeInsideInterfaceDeclarationsForServices { get; }
    Func<string, string> InsertCodeInsideConstructorForDaos { get; }
    Func<string, string> InsertCodeInsideConstructorForServiceTypes { get; }
    Func<string, string> InsertCodeInsideConstructorForServices { get; }
    Func<string, string> InsertCodeInsideClassForDaos { get; }
    Func<string, string> InsertCodeInsideClassServiceTypes { get; }
    Func<string, string> InsertCodeInsideClassForServices { get; }
    Func<string, string, string> InsertCodeBeforePropertyForDaos { get; }
    Func<string, string, string> InsertCodeBeforePropertyServiceTypes { get; }
    Func<string, string, string> InsertCodeBeforePropertyForServices { get; }
    Func<string, string> ModifyBaseTypeForDaos { get; }
    Func<string, string> ModifyBaseTypeForServiceTypes { get; }
    Func<string, string> ModifyBaseTypeForForServices { get; }
    Func<string, string, string> ModifyPropertyTypeForDaos { get; }
    Func<string, string, string> ModifyPropertyTypeForServiceTypes { get; }
    Func<string, string, string> ModifyPropertyTypeForServices { get; }
    Func<string, string, string> ModifyDefaultValueForPropertyTypeForDaos { get; }
    Func<string, string, string> ModifyDefaultValueForPropertyTypeForServiceTypes { get; }
    Func<string, string, string> ModifyDefaultValueForPropertyTypeForServices { get; }
}

public class CodeGenConfig : ICodeGenConfig
{
    private static readonly PluralizationService Pluralizer;
    static CodeGenConfig()
    {
        Pluralizer = PluralizationService.CreateService(CultureInfo.CurrentUICulture);
    }
    public string OutputDir { get; set; }
    public string ConnectionString { get; set; }
    public string Dialect { get; set; }
    public string Namespace { get; set; }
    public string DaosNamespace { get; set; }
    public string ServiceTypesNamespace { get; set; }
    public string ServicesNamespace { get; set; }
    public bool GenerateDaos { get; set; }
    public bool GenerateServiceTypes { get; set; }
    public bool GenerateServices { get; set; }
    public string DaosFile { get; set; }
    public string ServiceTypesFile { get; set; }
    public string ServicesFile { get; set; }
    public bool IncludeForeignKeyDeleteAndUpdateRules { get; set; }
    public bool OmitSchemaAnnotationIfOnlyOneSchemaExists { get; set; }
    public List<string> ExtraUsingNamespacesForDaos { get; set; }
    public List<string> ExtraUsingNamespacesForServiceTypes { get; set; }
    public List<string> ExtraUsingNamespacesForServices { get; set; }
    public Func<string, bool> ExcludeSchemaFilter { get; set; } = schema => false;
    public Func<string, string, bool> ExcludeTableFilter { get; set; } = (schema, table) => false;
    public Func<string, string, string, bool> ExcludeTableColumnFilter { get; set; } = (schema, table, column) => false;
    public Func<string, string, string> TableNameToClassNameConverter { get; set; } = (schema, table) =>
    {
        var nm = table.Replace(" ", "");
        return Pluralizer.IsPlural(nm) ? Pluralizer.Singularize(nm) : nm;
    };
    public Func<string, string, string> TableNameToCollectionNameConverter { get; set; } = (schema, table) =>
    {
        var nm = table.Replace(" ", "");
        return Pluralizer.IsPlural(nm) ? nm : Pluralizer.Pluralize(nm);
    };
    public Func<string, string, string, string> TableColumnNameToPropertyNameConverter { get; set; } = (schema, table, column) =>
    {
        var nm = column.Replace(" ", "");

        if (Utils.CSharpKeywords.Contains(nm.ToLower()) || nm.Equals(table, StringComparison.OrdinalIgnoreCase))
            nm = "_" + nm;
        if (nm.EndsWith("ID"))
            nm = nm.Substring(0, nm.Length - 1) + "d";
        return nm;
    };
    public Func<string, string> ForeignKeyPropertyToForeignKeyReferenceName { get; set; } = fkPropertyName => fkPropertyName.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ? fkPropertyName.Substring(0, fkPropertyName.Length - 2) : fkPropertyName + "_";
    public Func<string, string, string, bool> IncludeForeignKeyReference { get; set; } = (schema, table, column) => true;
    public Func<string, string, string, string, bool> IncludeCollectionReference { get; set; } = (schema, table, pkTable, pkColumn) => true;
    public Func<string> InsertCodeAfterUsingStatementsForDaos { get; set; }
    public Func<string> InsertCodeAfterUsingStatementsForServiceTypes { get; set; }
    public Func<string> InsertCodeAfterUsingStatementsForServices { get; set; }
    public Func<string, string> InsertCodeBeforeClassDeclarationForDaos { get; set; }
    public Func<string, string> InsertCodeBeforeClassDeclarationForServiceTypes { get; set; }
    public Func<string, string> InsertCodeBeforeClassDeclarationForServices { get; set; }
    public Func<string, string> InsertCodeInsideInterfaceDeclarationsForDaos { get; set; }
    public Func<string, string> InsertCodeInsideInterfaceDeclarationsForServiceTypes { get; set; }
    public Func<string, string> InsertCodeInsideInterfaceDeclarationsForServices { get; set; }
    public Func<string, string> InsertCodeInsideConstructorForDaos { get; set; }
    public Func<string, string> InsertCodeInsideConstructorForServiceTypes { get; set; }
    public Func<string, string> InsertCodeInsideConstructorForServices { get; set; }
    public Func<string, string> InsertCodeInsideClassForDaos { get; set; }
    public Func<string, string> InsertCodeInsideClassServiceTypes { get; set; }
    public Func<string, string> InsertCodeInsideClassForServices { get; set; }
    public Func<string, string, string> InsertCodeBeforePropertyForDaos { get; set; }
    public Func<string, string, string> InsertCodeBeforePropertyServiceTypes { get; set; }
    public Func<string, string, string> InsertCodeBeforePropertyForServices { get; set; }
    public Func<string, string> ModifyBaseTypeForDaos { get; set; }
    public Func<string, string> ModifyBaseTypeForServiceTypes { get; set; }
    public Func<string, string> ModifyBaseTypeForForServices { get; set; }
    public Func<string, string, string> ModifyPropertyTypeForDaos { get; set; }
    public Func<string, string, string> ModifyPropertyTypeForServiceTypes { get; set; }
    public Func<string, string, string> ModifyPropertyTypeForServices { get; set; }
    public Func<string, string, string> ModifyDefaultValueForPropertyTypeForDaos { get; set; }
    public Func<string, string, string> ModifyDefaultValueForPropertyTypeForServiceTypes { get; set; }
    public Func<string, string, string> ModifyDefaultValueForPropertyTypeForServices { get; set; }
}

class CodeGenerator
{
    private readonly ICodeGenConfig _config;
    private readonly SqlType _sqlType;
    private readonly string _sqlTypeVariant;

    public CodeGenerator(ICodeGenConfig config)
    {
        _config = config;
        _sqlType = Utils.GetSqlType(_config.Dialect, out _sqlTypeVariant);
    }
    public DatabaseSchema MetaDb { get; set; }

    internal void Execute()
    {
        using (var reader = new DatabaseReader(_config.ConnectionString, _sqlType))
        {
            MetaDb = reader.ReadAll();
            if (_config.GenerateDaos) GenerateDaos();
            if (_config.GenerateServiceTypes) GenerateServiceTypes();
            if (_config.GenerateServices) GenerateServices();
        }
    }

    private void GenerateDaos()
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

    private void GenerateServiceTypes()
    {
    }

    private void GenerateServices()
    {
    }

    #region Simplified representations of class structures

    public class XFile
    {
        public string Name { get; set; }

        public string FileCommentsBlock { get; set; } = string.Empty;

        public List<XUsingStatement> UsingStatements { get; set; } = new List<XUsingStatement>();

        public string PostUsingStatementsBlock { get; set; } = string.Empty;

        public List<XNamespaceBlock> NamespaceBlocks { get; set; } = new List<XNamespaceBlock>();

        public override string ToString()
        {
            var sb = new StringBuilder();

            if (string.IsNullOrWhiteSpace(FileCommentsBlock))
            {
                sb.AppendFormat(@"
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated 
//          by {0}
//          on {1}
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------
", Assembly.GetExecutingAssembly().GetName().Name, DateTime.Now.ToString("R"));
            }
            else
            {
                sb.AppendLine(FileCommentsBlock);
            }

            foreach (var usingStatement in UsingStatements ?? new List<XUsingStatement>())
            {
                sb.AppendLine(usingStatement.ToString());
            }

            sb.AppendLine(PostUsingStatementsBlock ?? string.Empty);

            foreach (var namespaceBlock in NamespaceBlocks ?? new List<XNamespaceBlock>())
            {
                sb.AppendLine(namespaceBlock.ToString());
                sb.AppendLine(string.Empty);
            }

            return sb.ToString();
        }
    }

    public class XUsingStatement
    {
        public string Namespace { get; set; }

        public override string ToString()
        {
            return Namespace == null ? "" : $"using {Namespace};";
        }
    }

    public class XNamespaceBlock
    {
        public string Name { get; set; }
        public List<XNamespacePart> Parts { get; set; } = new List<XNamespacePart>();

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"namespace {Name};");
            sb.AppendLine("{");
            foreach (var part in Parts)
            {
                sb.AppendLine(Utils.Indent(part.ToString()));
            }
            sb.AppendLine("}");
            return sb.ToString();
        }
    }

    public class XAttribute
    {
        public string Name { get; set; }
        public List<string> Arguments { get; set; }

        public override string ToString()
        {
            return ToString(false);
        }

        public string ToString(bool excludeBrackets)
        {
            var sb = new StringBuilder();

            if (!excludeBrackets) sb.Append("[");
            sb.Append(Name);
            if (Arguments != null && Arguments.Count > 0) sb.AppendFormat("({0})", string.Join(", ", Arguments));
            if (!excludeBrackets) sb.Append("]");
            return sb.ToString();
        }
    }

    public interface IXPartWithAttributes
    {
        List<XAttribute> Attributes { get; set; }
    }

    public interface IXPartWithInterfaces
    {
        List<string> Interfaces { get; set; }
    }

    public interface IXPartWithSummaryComment
    {
        string SummaryComment { get; set; }
    }

    public enum XMemberType
    {
        Public,
        Private,
        Internal,
        Protected
    }

    public enum XMemberModifier
    {
        Abstract,
        Virtual,
        ReadOnly,
        Static,
        Volatile
    }

    public interface IXMemberPart
    {
        string Name { get; set; }
        XMemberType MemberType { get; set; }
        List<XMemberModifier> MemberModifiers { get; set; }
    }

    public class XClass : IXPartWithAttributes, IXPartWithInterfaces, IXPartWithSummaryComment, IXMemberPart
    {
        public string SummaryComment { get; set; }

        public List<XAttribute> Attributes { get; set; } = new List<XAttribute>();
        public string Name { get; set; }
        public XMemberType MemberType { get; set; }
        public List<XMemberModifier> MemberModifiers { get; set; }
        public string BaseClass { get; set; }

        public List<string> Interfaces { get; set; } = new List<string>();
        public string PostInterfacesBlock { get; set; }
        public string PreFieldsBlock { get; set; }

        public List<IXMemberPart> Fields { get; set; } = new List<IXMemberPart>();
        public string PostFieldsBlock { get; set; }
        public string PrePropertiesBlock { get; set; }

        public List<IXMemberPart> Properties { get; set; } = new List<IXMemberPart>();
        public string PropertiesBlock { get; set; }
        public string PreMethodsBlock { get; set; }

        public List<IXMemberPart> Methods { get; set; } = new List<IXMemberPart>();
    }

    public class XMethod : IXPartWithAttributes, IXPartWithSummaryComment, IXMemberPart
    {
    }

    public class XProperty : IXPartWithAttributes, IXPartWithSummaryComment, IXMemberPart
    {
    }

    #endregion

    // tableName, <DatabaseTable, schemaName, className, collectionName>
    Dictionary<string, Tuple<DatabaseTable, string, string, string>> tableMap = new Dictionary<string, Tuple<DatabaseTable, string, string, string>>();

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
}

static class Utils
{
    public static string Indent(string textToIndent, int indentAmount = 4)
    {
        var indent = new string(' ', indentAmount);

        if (Environment.NewLine.EqualsIgnoreCase("\r"))
            textToIndent = textToIndent.Replace("\n", Environment.NewLine);
        if (Environment.NewLine.EqualsIgnoreCase("\n"))
            textToIndent = textToIndent.Replace("\r", Environment.NewLine);
        return indent + textToIndent.Replace(Environment.NewLine, Environment.NewLine + indent);
    }

    public static readonly string[] CSharpKeywords = new[] {
        "abstract", "add", "as", "ascending", "async", "await", "base", "bool", "break", "by", "byte", "case",
        "catch", "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "descending",
        "do", "double", "dynamic", "else", "enum", "equals", "explicit", "extern", "false", "finally", "fixed",
        "float", "for", "foreach", "from", "get", "global  goto", "group", "if", "implicit", "in", "int", "interface",
        "internal", "into", "is", "join", "let", "lock", "long", "namespace", "new", "null", "object", "on", "operator",
        "orderby", "out", "override", "params", "partial", "private", "protected", "public", "readonly", "ref",
        "remove", "return", "sbyte", "sealed", "select", "set", "short", "sizeof", "stackalloc", "static", "string",
        "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
        "using", "value", "var", "virtual", "void", "volatile", "where", "while", "yield" };

    public static readonly string[] DefaultUsingNamespaces = new[] {
        "System", "System.Collections.Generic", "ServiceStack.DataAnnotations"
    };

    public static bool IsNullableType(string csharpTypeName)
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

    internal static SqlType GetSqlType(string dialect, out string sqlTypeVariant)
    {
        sqlTypeVariant = null;

        SqlType? sqlType = null;

        dialect = dialect.ToLowerInvariant();

        if (dialect.Contains("sqlserver"))
        {
            sqlType = SqlType.SqlServer;
            if (dialect.IndexOf("ce", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                sqlType = SqlType.SqlServerCe;
                sqlTypeVariant = "CE";
            }
            else if (dialect.Contains("2005"))
                sqlTypeVariant = "2005";
            else if (dialect.Contains("2008"))
                sqlTypeVariant = "2008";
            else if (dialect.Contains("2012"))
                sqlTypeVariant = "2012";
            else if (dialect.Contains("2014"))
                sqlTypeVariant = "2014";
            else if (dialect.Contains("2016"))
                sqlTypeVariant = "2016";
        }
        else if (dialect.Contains("mysql"))
        {
            sqlType = SqlType.MySql;
        }
        else if (dialect.Contains("pgsql") || dialect.Contains("postgresql"))
        {
            sqlType = SqlType.PostgreSql;
        }
        else if (dialect.Contains("oracle"))
        {
            sqlType = SqlType.Oracle;
        }
        else if (dialect.Contains("sqlite"))
        {
            sqlType = SqlType.SQLite;
        }

        if (!sqlType.HasValue)
            throw new ArgumentException(nameof(dialect));

        return sqlType.Value;
    }
}

#region Console Argument parsing
// Adapted from: MadProps.AppArgs
static class ConsoleArgs
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

static class ConsoleArgsPropertyInfoExtensions
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
#endregion