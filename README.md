# codegen-servicestack-from-database
A simple API code generator for ServiceStack (from database) - Use and modify on a project basis, meant as a starter template ...

## get started

Unzip the cs-script utility *.7z files inside the lib directory (or install the latest version from [cs-script](https://github.com/oleg-shilo/cs-script/releases)).
  - Read about cs-script on the [wiki](https://github.com/oleg-shilo/cs-script/wiki) and read the [docs](http://www.csscript.net/help/Online/) to learn about this awesome tool
  - You could use [LINQPad](https://www.linqpad.net/) or [scriptcs](http://scriptcs.net/) as well (leaving that up to you to convert 'codegen.cs')

Once you have either installed cs-script or unzipped it, you can run the '[codegen.cs](codegen.cs)' command from the command prompt:

```
set CSSCRIPT_DIR=%cd%\lib\cs-script
set path=%CSSCRIPT_DIR%;%path%
cscs.exe codegen.cs -help
```

### sample commands

- Generate code using a connection string and a database dialect

```
cscs codegen.cs -namespace=MyApp -dialect=sqlserver2012 -connectionstring="Data Source=.\\sqlexpress;Initial Catalog=MyAppDB;Integrated Security=True"
```

- Create a starter config file to customize the code generated output

```
cscs codegen.cs -create-config=myappconfig.json
```

- Generate code using a config file

```
cscs codegen.cs -config=myappconfig.json"
```

- Generate code to a specific directory or file (if a file, the file must have the '.cs' extension)

```
cscs codegen.cs -namespace=MyApp -dialect=sqlserver2012 -connectionstring="Data Source=.\\sqlexpress;Initial Catalog=MyAppDB;Integrated Security=True" -output=.\generated-code\code.cs
```



  
