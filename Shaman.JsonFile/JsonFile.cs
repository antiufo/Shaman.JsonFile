﻿using Microsoft.CSharp.RuntimeBinder;
using Newtonsoft.Json;
#if !STANDALONE
using Shaman.Rest;
using Shaman.Runtime.ReflectionExtensions;
using Shaman.Types;
#endif
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if !CORECLR
using System.Windows.Forms;
#endif
using static Shaman.Runtime.ReplExtensions;

namespace Shaman.Runtime
{

#if !STANDALONE
    [RestrictedAccess]
#endif
    public static class JsonFile
    {
        static JsonFile()
        {
        }


        public enum Format
        {
            FormattedJson = 0,
            CompactJson = 1,
#if BSON
            Bson = 2,
#endif
#if PROTOBUF
            Protobuf = 3,
#endif
            Automatic = 4
        }

        [StaticFieldCategory(StaticFieldCategory.TODO)]
        internal static readonly ConcurrentDictionary<string, JsonFileInternal> OpenFiles = new ConcurrentDictionary<string, JsonFileInternal>();

        [StaticFieldCategory(StaticFieldCategory.Stable)]
        private static bool basePathLoaded;
        [StaticFieldCategory(StaticFieldCategory.Stable)]
        private static string basePath;

        [Configuration]
        public readonly static string Configuration_BasePath = null;

        public static string BasePath
        {
            get
            {
                if (!basePathLoaded)
                {
#if STANDALONE
                    var entrypointDirectory = Directory.GetCurrentDirectory();
#else
                    var entrypointDirectory = ConfigurationManager.EntrypointDirectory;
#endif
                    if (Configuration_BasePath != null)
                    {
                        basePath = Path.Combine(entrypointDirectory, Configuration_BasePath);
                    }
                    else
                    {
                        var folderName = Path.GetFileName(entrypointDirectory);
                        basePath =
                            folderName.Equals("Release", StringComparison.OrdinalIgnoreCase) ||
                            folderName.Equals("Debug", StringComparison.OrdinalIgnoreCase)
                                ? Path.Combine(entrypointDirectory, @"../..")
                                : entrypointDirectory;
                    }
                    basePathLoaded = true;
                }
                return basePath;
            }
            set
            {
                if (value != null) Directory.CreateDirectory(value);
                basePath = value;
                basePathLoaded = true;
            }
        }


        public static JsonFile<T> Open<T>(string path)
        {
            return Open<T>(path, Format.Automatic);
        }

        public static T Read<T>(string path)
        {
            return Read<T>(path, Format.Automatic);
        }

        public static JsonFile<T> Open<T>(string path, Format format)
        {
            path = GetPath(path);
            var normalized = Path.GetFullPath(path).ToLowerFast();
            if (format == Format.Automatic) format = GetFormatFromExtension(path);
            var file = (JsonFileInternal<T>)OpenFiles.GetOrAdd(normalized, dummy => new JsonFileInternal<T>(path, normalized, format));
            if (file.threadId != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Attempt to open an already open JsonFile on a different thread.");
            file.AcquireReference();
            return new JsonFile<T>(file);
        }
        
        public static T Read<T>(string path, Format format)
        {
            path = GetPath(path);
            if (format == Format.Automatic) format = GetFormatFromExtension(path);
            var a = new JsonFileInternal<T>(path, null, format);
            return a.Content;
        }


        public static JsonFile<List<T>> OpenList<T>(string path)
        {
            return OpenList<T>(path, Format.Automatic);
        }
        public static JsonFile<List<T>> OpenList<T>(string path, Format format)
        {
            return Open<List<T>>(path, format);
        }
        
        public static List<T> ReadList<T>(string path)
        {
            return Read<List<T>>(path, Format.Automatic);
        }
        public static List<T> ReadList<T>(string path, Format format)
        {
            return Read<List<T>>(path, format);
        }

        private const Format DEFAULT_JSON_FORMAT = Format.FormattedJson;

        public static JsonFile<T> Open<T>()
        {
            return Open<T>(GetPath(typeof(T), DEFAULT_JSON_FORMAT), DEFAULT_JSON_FORMAT);
        }
        public static JsonFile<List<T>> OpenList<T>()
        {
            return Open<List<T>>(GetPath(typeof(List<T>), DEFAULT_JSON_FORMAT), DEFAULT_JSON_FORMAT);
        }
        
        
        public static T Read<T>()
        {
            return Read<T>(GetPath(typeof(T), DEFAULT_JSON_FORMAT), DEFAULT_JSON_FORMAT);
        }
        public static List<T> ReadList<T>()
        {
            return Read<List<T>>(GetPath(typeof(List<T>), DEFAULT_JSON_FORMAT), DEFAULT_JSON_FORMAT);
        }

        private static string GetPath(Type type, Format format)
        {
            var ext = ".json";
#if BSON
            if (format == Format.BSON) ext = ".bson";
#endif
#if PROTOBUF
            if (format == Format.Protobuf) ext = ".pb";
#endif
            return GetPath(GetFriendlyTypeName(type) + ext);
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (type.GetTypeInfo().IsGenericType)
            {
                var generic = type.GetGenericTypeDefinition();
                var name = generic.Namespace == "System.Collections.Generic" ? generic.Name : generic.FullName;
                var idx = name.LastIndexOf('`');
                if (idx != -1) name = name.Substring(0, idx);
                return name + "(" + string.Join(",", type.GetGenericArguments().Select(x => GetFriendlyTypeName(x))) + ")";
            }
            return type.FullName;
        }

        private static string GetPath(string path)
        {
            if (BasePath != null && !Path.IsPathRooted(path)) path = Path.Combine(BasePath, path);
            return System.IO.Path.GetFullPath(path);
        }


        public static void Save(object obj)
        {
            Save(obj, GetPath(obj.GetType(), DEFAULT_JSON_FORMAT), DEFAULT_JSON_FORMAT);
        }

        private static Format GetFormatFromExtension(string path)
        {
            var ext = Path.GetExtension(path);
            if (ext == null) return DEFAULT_JSON_FORMAT;
#if BSON
            if (ext.Equals(".bson")) return Format.Bson;
#endif
#if PROTOBUF
            if (ext.Equals(".pb")) return Format.Protobuf;
#endif
            return DEFAULT_JSON_FORMAT;
        }

        public static void Save(object obj, string path)
        {
            Save(obj, path, GetFormatFromExtension(path));
        }

#if PROTOBUF
        [StaticFieldCategory(StaticFieldCategory.Stable)]
        private static MethodInfo _protobufSerializeOfTMethod;
        internal static MethodInfo ProtobufSerializeOfTMethod => _protobufSerializeOfTMethod ?? (_protobufSerializeOfTMethod = typeof(ProtoBuf.Serializer).GetRuntimeMethods().Single(x => x.Name == "Serialize" && x.GetParameters().Length == 2 && x.GetParameters()[0].ParameterType == typeof(Stream)));
#endif

        public static void Save(object obj, string path, Format format)
        {
            path = GetPath(path);
            var tempPath = Path.Combine(Path.GetDirectoryName(path), "$" + Path.GetFileName(path) + ".tmp");
#if BSON
            if (format == Format.Bson)
            {
                throw Sanity.NotImplementedException();
            }
#endif

#if PROTOBUF
            if (format == Format.Protobuf)
            {
                path = GetPath(path);
                using (var stream = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.Delete))
                {
                    ProtobufSerializeOfTMethod.MakeGenericMethodFast(obj.GetType()).Invoke(null, new[] { stream, obj });
                }
                File.Delete(path);
                File.Move(tempPath, path);
                return;
            }
#endif



            {
                path = GetPath(path);
                using (var stream = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.Delete))
                using (var sw = new StreamWriter(stream, Encoding.UTF8))
                {
                    var js = new JsonSerializer();
#if !STANDALONE
                    js.AddAwdeeConverters();
#endif
                    js.Formatting = format == Format.CompactJson ? Formatting.None : Formatting.Indented;
                    js.Serialize(sw, obj);
                }
                File.Delete(path);
                File.Move(tempPath, path);
                return;
            }


        }
        
    }

    public class JsonFile<T> : IDisposable
    {
        private JsonFileInternal<T> data;
        internal JsonFile(JsonFileInternal<T> data)
        {
            this.data = data;
        }

        private long changeCount = 0;

        public void DiscardAll()
        {
            this.data.DiscardAll();
        }

        public long MaximumUncommittedChanges { get; set; } = -1;
        public TimeSpan MaximumUncommittedTime { get; set; } = TimeSpan.FromSeconds(60);
        public long ChangeCount { get { return changeCount; } }

        public void IncrementChangeCount()
        {
            changeCount++;
            if (lastSaved == null) lastSaved = Stopwatch.StartNew();
        }

        public void IncrementChangeCountAndMaybeSave()
        {
            changeCount++;
            if (lastSaved == null) lastSaved = Stopwatch.StartNew();
            MaybeSave();
        }

        public void MaybeSave()
        {
            if (
                (MaximumUncommittedChanges != -1 && changeCount >= MaximumUncommittedChanges) ||
                (MaximumUncommittedTime.Ticks != 0 && lastSaved != null && lastSaved.Elapsed > MaximumUncommittedTime)
                )
                this.Save();
        }

        public T Content
        {
            get
            {
                if (data == null) throw new ObjectDisposedException("JsonFile");
                return data.Content;
            }
            set
            {
                if (data == null) throw new ObjectDisposedException("JsonFile");
                data.Content = value;
            }
        }

        public void Dispose()
        {

            if (data != null)
            {
                if (Environment.CurrentManagedThreadId != data.threadId) throw new InvalidOperationException("Attempt to dispose a JsonFile from a wrong thread.");

                data.ReleaseReference();
                data = null;
            }
        }

        Stopwatch lastSaved;

        public void Save()
        {
            changeCount = 0;
            if (lastSaved != null) lastSaved.Restart();
            data.Save();
        }

        public void MigrateToFormat(JsonFile.Format destinationFormat)
        {
            data.MigrateToFormat(destinationFormat);
        }
    }

    internal abstract class JsonFileInternal
    {


        public abstract void AcquireReference();
        public abstract void ReleaseReference();


    }

    internal class JsonFileInternal<T> : JsonFileInternal
    {

        private int usageCount;
        private string key;
        private volatile string path;
        private string tempPath;
        private string transactionFile;
        private string lastWrittenJson;
        internal readonly int threadId;

        private T content;

        private JsonFile.Format format;

        internal JsonFileInternal(string path, string key, JsonFile.Format format)
        {
            this.threadId = Environment.CurrentManagedThreadId;
            this.format = format;
            this.key = key;
            this.path = path;
            this.tempPath = Path.Combine(Path.GetDirectoryName(path), "$" + Path.GetFileName(path) + ".tmp");
            this.transactionFile = path + ".transaction";
            if (File.Exists(transactionFile))
            {
                File.Move(transactionFile, path);
            }

            if (File.Exists(path))
            {
#if BSON
                if (format == JsonFile.Format.Bson)
                {

                    using (var stream = File.OpenRead(path))
                    using (var reader = new BsonReader(stream))
                    {
                        var deserializer = new JsonSerializer();
                        this.content = deserializer.Deserialize<T>(reader);
                    }
                }
                else
#endif
#if PROTOBUF
                if (format == JsonFile.Format.Protobuf)
                {
                    using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Delete | FileShare.Read))
                    {
                        this.content = ProtoBuf.Serializer.Deserialize<T>(stream);
                    }

                }
                else
#endif
                {

                    this.lastWrittenJson = File.ReadAllText(path);
                    this.content = JsonConvert.DeserializeObject<T>(this.lastWrittenJson);
                }
            }
            else
            {
                this.content = Activator.CreateInstance<T>();
                if (key != null) Save(true); // Don't create file for Read<T>()
            }
        }

        public T Content
        {
            get
            {
                if (path == null) throw new ObjectDisposedException("JsonFile");
                return content;
            }
            set
            {
                if (path == null) throw new ObjectDisposedException("JsonFile");
                this.content = value;
            }
        }

        public override void AcquireReference()
        {
            usageCount++;
        }

        internal void Save(bool isNew = false)
        {
            if (path == null) throw new InvalidOperationException();
            bool shouldSave = false;

#if BSON
            if (format == JsonFile.Format.Bson)
            {
                using (var stream = File.OpenWrite(tempPath))
                using (var writer = new BsonWriter(stream))
                {
                    var s = new JsonSerializer();
                    s.Serialize(writer, content);
                }
            }
            else
            
#endif
#if PROTOBUF
            if (format == JsonFile.Format.Protobuf)
            {
                using (var stream = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.Delete))
                {
                    ProtoBuf.Serializer.Serialize(stream, content);
                }
                shouldSave = true;
            }
            else
#endif
            if (format == JsonFile.Format.CompactJson || format == JsonFile.Format.FormattedJson)
            {
                var json = JsonConvert.SerializeObject(content, format == JsonFile.Format.FormattedJson ? Formatting.Indented : Formatting.None);
                if (json != lastWrittenJson)
                {

                    File.WriteAllText(tempPath, json, Encoding.UTF8);
                    lastWrittenJson = json;
                    shouldSave = true;
                }
            }
            else
            {
#if STANDALONE
                throw new ArgumentException();
#else
                Sanity.ShouldntHaveHappened();
#endif
            }

            if (shouldSave)
            {
                if (isNew)
                {
                    File.Move(tempPath, path);
                }
                else
                {
                    File.Move(path, transactionFile);
                    File.Move(tempPath, path);
                    File.Delete(transactionFile);
                }
            }

        }

        public override void ReleaseReference()
        {
            if (--usageCount == 0)
            {
                if (path != null) Save();
                this.path = null;
                JsonFileInternal dummy;
                JsonFile.OpenFiles.TryRemove(key, out dummy);
                content = default(T);
            }

        }

        public void DiscardAll()
        {
            this.path = null;
            JsonFileInternal dummy;
            JsonFile.OpenFiles.TryRemove(key, out dummy);
            content = default(T);
        }


        public void MigrateToFormat(JsonFile.Format destinationFormat)
        {
            lastWrittenJson = null;
            format = destinationFormat;
            Save();
        }

    }

#if !STANDALONE
    [RestrictedAccess]
#endif
    public static class ReplExtensions
    {
#if !CORECLR
        public static string CopyText(this string text)
        {
            RunInSTA(() =>
            {
                if (string.IsNullOrEmpty(text)) Clipboard.Clear();
                Clipboard.SetText(text, System.Windows.Forms.TextDataFormat.UnicodeText);
            });
            return text;
        }

        public static string PasteAsText()
        {
            string content = null;
            RunInSTA(() => { content = Clipboard.GetText(); });
            return string.IsNullOrEmpty(content) ? null : content;
        }
#endif

        public static void ViewJson(this object obj)
        {
            Console.WriteLine(obj.ToJson());
        }


        public static string ToJson(this object obj)
        {
            return JsonConvert.SerializeObject(obj, Formatting.Indented);
        }
        
#if !CORECLR
        private static void RunInSTA(Action action)
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                var s = new Thread(() => action());
                s.Name = "RunInSTA";
                s.SetApartmentState(ApartmentState.STA);
                s.IsBackground = true;
                s.Start();
                s.Join();
                return;
            }
            action();
        }

        public static T CopyJson<T>(this T obj)
        {
            CopyText(ToJson(obj));
            return obj;
        }
        public static IEnumerable CopyTable(this IEnumerable items)
        {
            CopyText(ToTable(items));
            return items;
        }
        public static IEnumerable<T> CopyTable<T>(this IEnumerable<T> items)
        {
            CopyText(ToTable(items));
            return items;
        }

        public static IEnumerable CopyTable<T>(this IEnumerable items, string caption)
        {
            CopyText(caption + "\n" + ToTable(items));
            return items;
        }
        public static IEnumerable<T> CopyTable<T>(this IEnumerable<T> items, string caption)
        {
            CopyText(caption + "\n" + ToTable(items));
            return items;
        }
#endif

        private const char OUTPUT_ALIGNED = (char)0;
        private const char OUTPUT_CSV = (char)1;

        public static IEnumerable<T> SaveTable<T>(this IEnumerable<T> items, string fileName)
        {
            ContinuationInfo dummy = null;
            return SaveTable<T>(items, fileName, ref dummy);
        }

        public class ContinuationInfo
        {
            internal ContinuationInfo() { }
            internal List<Field> fields;
            internal bool headersWritten;
        }

        public static IEnumerable<T> SaveTable<T>(this IEnumerable<T> items, string fileName, ref ContinuationInfo continuationInfo)
        {
            if (fileName.EndsWith(".xlsx") || fileName.EndsWith(".xls"))
            {
                if (fileName.StartsWith("CON:")) return OpenExcel(items);
                return SaveExcel(items, fileName);
            }

            var writer = fileName.StartsWith("CLIPBOARD:") ? new StringWriter() : fileName.StartsWith("CON:") ? Console.Out : null;

            using (var fileout = writer != null ? null : new StreamWriter(File.Open(fileName, FileMode.Create, FileAccess.Write, FileShare.Delete | FileShare.Read), Encoding.UTF8))
            {
                if (writer == null) writer = fileout;
                if (fileName.EndsWith(".json"))
                {
                    var serializer = new JsonSerializer();
#if !STANDALONE
                    serializer.AddAwdeeConverters();
#endif
                    serializer.Formatting = Formatting.Indented;
                    serializer.Serialize(writer, items.ToList());
                    writer.WriteLine();
                }
                else
                {
                    if (continuationInfo == null) continuationInfo = new ContinuationInfo();
                    ToTable(items, typeof(T), writer, fileName.EndsWith(".txt-preformatted") ? OUTPUT_ALIGNED : fileName.EndsWith(".csv") ? OUTPUT_CSV : '\t', writer == Console.Out, continuationInfo);
                }
            }

            if (fileName.StartsWith("CLIPBOARD:"))
            {
#if CORECLR
                throw new PlatformNotSupportedException("Copy to clipboard is not implemented for .NET standard.");
#else
                var t = ((StringWriter)writer).ToString();
                CopyText(t);
#endif
            }
            return items;
        }

        public static void ViewTable(this IEnumerable items)
        {
            Console.WriteLine(ToTable(items, OUTPUT_ALIGNED, true));
        }

        public static void ViewTable<T>(this IEnumerable<T> items)
        {
            Console.WriteLine(ToTable(items, OUTPUT_ALIGNED, true));
        }

        internal class Field
        {
            public string Name;
            public object RawField;
            public Func<object, object> Get;
            public Type Type;
            public int ColumnWidth;
            public bool IsNumeric;
        }

        internal static List<Field> GetFields(Type type)
        {
#if !STANDALONE
            if (type.Is<Entity>())
            {
                return EntityType.FromNativeType(type).Fields.Select(x => new Field()
                {
                    Name = x.Name,
                    RawField = x,
                    Get = new Func<object, object>(y =>
                    {
                        var ent = (Entity)y;
                        return x.IsStoredIn(ent) ? x.GetStoredValueDirect(ent) : null;
                    }),
                    Type = x.FieldType.NativeType
                }).ToList();
            };
#endif

            var k = Nullable.GetUnderlyingType(type) ?? type;
            if (type == typeof(string) || k.IsPrimitive || type == typeof(object))
                return new List<Field>() { new Field() { Name = "Value", Type = type, IsNumeric = IsNumeric(type), Get = x => x } };

            var emptyArray = new object[] { };
            var fields =
            type.GetTypeInfo().IsPrimitive || type.GetTypeInfo().IsEnum || type == typeof(string) ? new[] { new Field { Name = "Value", Get = new Func<object, object>(y => y), Type = type } }.ToList() :
            type.GetFields(BindingFlags.Instance | BindingFlags.Public)
#if !STANDALONE
            .Where(x => x.GetCustomAttribute<RestrictedAccessAttribute>() == null)
#endif
            .Select(x => new Field { Name = x.Name, RawField = x, Get = ReflectionHelper.GetGetter<object, object>(x), Type = x.FieldType })
            .Union(type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(x =>
                 x.GetIndexParameters().Length == 0
#if !STANDALONE
                 && x.DeclaringType != typeof(Entity)
                 && x.GetMethod.GetCustomAttribute<RestrictedAccessAttribute>() == null
#endif
)
             .Select(x => new Field { Name = x.Name, RawField = x, Get = ReflectionHelper.GetGetter<object, object>(x), Type = x.PropertyType }))
            .ToList();
            return fields;

        }

        [StaticFieldCategory(StaticFieldCategory.Stable)]
        internal static readonly Type[] NumericTypes = new[]{
           typeof(Byte),typeof(SByte),
           typeof(Int16),typeof(Int32),typeof(Int64),
           typeof(UInt16),typeof(UInt32),typeof(UInt64),
           typeof(Single),typeof(Double),typeof(Decimal)
       };

        private static bool IsNumeric(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return NumericTypes.Contains(type);
        }

        public static string ToTable<T>(this IEnumerable<T> items)
        {
            using (var sw = new StringWriter())
            {
                ToTable(items, typeof(T), sw, '\t', false);
                return sw.ToString();
            }
        }


        public static string ToTable(this IEnumerable items)
        {
            return ToTable(items, '\t', false);
        }

        private static string ToTable(this IEnumerable items, char separator, bool forConsole)
        {
            using (var sw = new StringWriter())
            {
                ToTable(items, GetEnumerableElementType(items), sw, separator, forConsole);
                return sw.ToString();
            }
        }

        public static void ToTable(IEnumerable items, Type type, TextWriter tw, char separator, bool forConsole)
        {
            var continuationInfo = new ContinuationInfo();
            ToTable(items, type, tw, separator, forConsole, continuationInfo);
        }




        private static void ToTable(IEnumerable items, Type type, TextWriter tw, char separator, bool forConsole, ContinuationInfo continuationInfo)
        {
            var enumerator = items.GetEnumerator();
            bool completed = false;
            var initial = new List<object>();
            try
            {
                if (separator == OUTPUT_ALIGNED)
                {
                    for (int i = 0; i < 50; i++)
                    {
                        if (!enumerator.MoveNext())
                        {
                            completed = true;
                            break;
                        }
                        else
                        {
                            initial.Add(enumerator.Current);
                        }
                    }
                }

                var fields = continuationInfo.fields;
                if (fields == null)
                {
                    fields = GetFields(type);
                    continuationInfo.fields = fields;

                    if (separator == OUTPUT_ALIGNED)
                    {
                        foreach (var field in fields)
                        {
                            field.ColumnWidth = field.Name.Length;
                            foreach (var item in initial)
                            {
                                var val = field.Get(item);
                                var str = RemoveForbiddenChars(val, separator);
                                if (str != null) field.ColumnWidth = Math.Max(field.ColumnWidth, str.Length);
                            }
                            var t = field.Type;
                            if (t.GetTypeInfo().IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>)) t = Nullable.GetUnderlyingType(t);
                            field.IsNumeric = NumericTypes.Contains(t);
                            field.ColumnWidth++;
                        }

                        if (forConsole)
                        {
                            var bufwidth = Console.BufferWidth;
                            var total = fields.Sum(x => x.ColumnWidth);

                            if (total > bufwidth)
                            {
                                var totalNumeric = fields.Where(x => x.IsNumeric).Sum(x => x.ColumnWidth);
                                var totalNonNumeric = total - totalNumeric;
                                
                                var availableForNonNumeric = bufwidth - totalNumeric;
                                var fairQuote = availableForNonNumeric / fields.Count(x => !x.IsNumeric);

                                var tofix = fields.Where(x => !x.IsNumeric && x.ColumnWidth > fairQuote).ToList();

                                var availableForRemaining = bufwidth - fields.Except(tofix).Sum(x => x.ColumnWidth);
                                var fairQuoteForRemaining = Math.Max(3, availableForRemaining / tofix.Count);

                                foreach (var item in tofix)
                                {
                                    item.ColumnWidth = fairQuoteForRemaining;
                                }

                            }

                        }
                    }
                }

                if (!continuationInfo.headersWritten && (fields.Count > 1 || separator == OUTPUT_CSV))
                {
                    continuationInfo.headersWritten = true;
                    for (int i = 0; i < fields.Count; i++)
                    {
                        if (i != 0)
                        {
                            if (separator == OUTPUT_CSV) tw.Write(',');
                            else if (separator != OUTPUT_ALIGNED) tw.Write(separator);
                        }
                        var field = fields[i];
                        var name = MaybeTrim(field.Name, field, separator);
                        tw.Write(name);

                        if (separator == OUTPUT_ALIGNED && i != fields.Count - 1)
                        {
                            for (var k = name.Length; k < field.ColumnWidth; k++)
                            {
                                tw.Write(' ');
                            }
                        }
                    }
                    tw.Write('\r');
                    tw.Write('\n');
                }
                var idx = 0;
                while (true)
                {
                    object item;
                    if (initial != null && idx < initial.Count)
                    {
                        item = initial[idx];
                    }
                    else
                    {
                        initial = null;
                        if (completed) break;
                        if (!enumerator.MoveNext()) break;
                        item = enumerator.Current;
                    }

                    for (int i = 0; i < fields.Count; i++)
                    {
                        var field = fields[i];

                        if (i != 0)
                        {
                            if (separator == OUTPUT_CSV) tw.Write(',');
                            else if (separator != OUTPUT_ALIGNED) tw.Write(separator);
                        }
                        var val = field.Get(item);
#if !STANDALONE
                        var es = val as EntitySet;
                        if (es != null) val = Utils.GetRestPath(es);
#endif
                        var str = RemoveForbiddenChars(val, separator);

                        str = MaybeTrim(str, field, separator);

                        var shouldWrap = separator == OUTPUT_CSV && (str.IndexOf('"') != -1 || str.IndexOf(',') != -1);
                        if (shouldWrap)
                        {
                            tw.Write('"');
                            str = str.Replace("\"", "\"\"");
                        }



                        if (!field.IsNumeric) tw.Write(str);
                        if (separator == OUTPUT_ALIGNED && (i != fields.Count - 1 || fields[fields.Count - 1].IsNumeric))
                        {
                            var k = str.Length;
                            if (field.IsNumeric) k++;
                            for (; k < field.ColumnWidth; k++)
                            {
                                tw.Write(' ');
                            }
                        }
                        if (field.IsNumeric) tw.Write(str);

                        if (separator == OUTPUT_ALIGNED && field.IsNumeric && i != fields.Count - 1)
                            tw.Write(' ');
                        if (shouldWrap) tw.Write('"');

                    }
                    tw.Write('\r');
                    tw.Write('\n');
                    idx++;
                }
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }
        }

        private static string MaybeTrim(string val, Field field, char separator)
        {
            if (separator != OUTPUT_ALIGNED) return val;
            var maxwidth = field.ColumnWidth - 1;
            if (val.Length > maxwidth) return val.Substring(0, maxwidth);
            return val;
        }

#if !STANDALONE
        [RestrictedAccess]
#endif
        public static IEnumerable<T> OpenExcel<T>(this IEnumerable<T> items)
        {
            var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
            SaveExcel(items, temp);
            Process.Start(temp);
            return items;
        }

        public static IEnumerable OpenExcel<T>(this IEnumerable items)
        {
            var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
            SaveExcel(items, temp);
            Process.Start(temp);
            return items;
        }




        public static IEnumerable<T> SaveExcel<T>(this IEnumerable<T> items, string xlsx)
        {
            if (xlsx.EndsWith(".xls")) throw new NotSupportedException("XLS is not supported, only XLSX.");
            var f = new OfficeOpenXml.ExcelPackage();
            var sheet = f.Workbook.Worksheets.Add("Sheet1");
            SaveExcel(items, sheet);
            f.SaveAs(new FileInfo(xlsx));
            return items;
        }


        public static IEnumerable SaveExcel(this IEnumerable items, string xlsx)
        {
            var f = new OfficeOpenXml.ExcelPackage();
            var sheet = f.Workbook.Worksheets.Add("Sheet1");
            SaveExcel(items, sheet, GetEnumerableElementType(items), null);
            f.SaveAs(new FileInfo(xlsx));
            return items;
        }

        private static Type GetEnumerableElementType(object ienumerable)
        {
            return ienumerable.GetType().GetInterfaces().First(x => x.GetTypeInfo().IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>)).GetGenericArguments()[0];

        }



        public static IEnumerable<T> SaveExcel<T>(this IEnumerable<T> items, OfficeOpenXml.ExcelWorksheet sheet)
        {
            SaveExcel(items, sheet, typeof(T), null);
            return items;
        }



        public static void SaveExcel(IEnumerable items, OfficeOpenXml.ExcelWorksheet sheet, Type elementType, string schemeAndAuthorityBase)
        {

            //var table = sheet.Tables.Single();




            var headerRow = sheet.Row(1);
            var fields = GetFields(elementType);

            var nextRow = 2;

            var col = 1;
            foreach (var field in fields)
            {
                var cell = sheet.Cells[1, col];
                cell.Value = field.Name;
                col++;
                var ft = field.Type;
                var ti = ft.GetTypeInfo();
                if (ti.IsGenericType && ft.GetGenericTypeDefinition() == typeof(Nullable<>)) ft = ft.GetGenericArguments()[0];
                sheet.Column(col - 1).Width = ti.IsPrimitive || NumericTypes.Contains(ft) || ti.IsEnum || ft == typeof(DateTime) || ft == typeof(DateTimeOffset) ? 10 : 25;
            }

            foreach (var item in items)
            {
                var row = nextRow;
                nextRow++;

                col = 1;
                foreach (var prop in fields)
                {

                    var cell = sheet.Cells[row, col];
                    var val = prop.Get(item);
#if !STANDALONE
                    if (val is EntitySet es)
                    {
                        if (schemeAndAuthorityBase != null)
                        {
                            var url = (schemeAndAuthorityBase + Utils.GetRestPath(es)).AsUri();
                            cell.Hyperlink = url;
                            cell.Value = url;
                        }
                        else
                        {
                            cell.Value = "(Collection)";
                        }
                    }
                    else if (val is Entity ent)
                    {
                        if (schemeAndAuthorityBase != null)
                        {
                            var url = (schemeAndAuthorityBase + Utils.GetRestPath(ent)).AsUri();
                            cell.Hyperlink = url;
                        }
                        cell.Value = ent.ToStringOrIdOrDefault(ent.EntityType.Name);
                    }
                    else
#endif
                    if (val is Uri url)
                    {
                        cell.Hyperlink = url;
                        cell.Value = url.AbsoluteUri;
                        //cell.Style.sy
                    }
#if !STANDALONE
                    else if (val is Money money)
                    {
                        cell.Value = money.CurrencyValue;
                        if (money.CurrencyType != Currency.None)
                            cell.Style.Numberformat.Format = Money.GetExcelFormat(money.CurrencyType);
                    }
                    else if (val is ICollection collection)
                    {
                        if (collection.Count == 0) cell.Value = null;
                        else if (collection.Count == 1) cell.Value = collection.Cast<object>().First();
                        else
                        {
                            var sb = ReseekableStringBuilder.AcquirePooledStringBuilder();
                            var first = true;
                            foreach (var z in collection)
                            {
                                if (!first) sb.Append(',');
                                sb.Append(z);
                                first = false;
                            }
                            cell.Value = ReseekableStringBuilder.GetValueAndRelease(sb);
                        }
                    }
#endif
                    else
                    {
                        cell.Value = val;
                        if (val is DateTime || val is DateTimeOffset)
                        {
                            cell.Style.Numberformat.Format = "yyyy-mm-dd HH hh:mm";
                        }

                    }

                    col++;
                }
            }
            var t = sheet.Tables.Add(new OfficeOpenXml.ExcelAddressBase(1, 1, nextRow - 1, col - 1), sheet.Name + "-Items");
            t.TableStyle = OfficeOpenXml.Table.TableStyles.None;
        }

/*
        public static string ToMatrixForPowershell(this IEnumerable<object> rows)
        {
            return ToMatrix(rows.SelectMany(
                row => (IEnumerable<object>)GetPowershellProperty(row, "Value", ref CallSiteRowValue), 
                (row, cell) => new { Row = Convert.ToString(GetPowershellProperty(row, "Key", ref CallSiteRowKey), CultureInfo.InvariantCulture), Cell = GetPowershellProperty(cell, "Value", ref CallSiteCellValue), Col = Convert.ToString(GetPowershellProperty(cell, "Key", ref CallSiteCellKey), CultureInfo.InvariantCulture) }),
                x => x.Row, x => x.Col, x => x.Cell);
        }


        static CallSite<Func<CallSite, object, object>> CallSiteRowKey;        
        static CallSite<Func<CallSite, object, object>> CallSiteRowValue;
        static CallSite<Func<CallSite, object, object>> CallSiteCellKey;
        static CallSite<Func<CallSite, object, object>> CallSiteCellValue;

        private static object GetPowershellProperty(object obj, string name, ref CallSite<Func<CallSite, object, object>> callsite)
        {
            try
            {
                var ht = obj as System.Collections.Hashtable;
                if (ht != null) return ht[name];
                var dyn = obj as System.Dynamic.IDynamicMetaObjectProvider;
                if (dyn != null)
                {
                    if(callsite == null)
                        callsite = CallSite<Func<CallSite, object, object>>.Create(Microsoft.CSharp.RuntimeBinder.Binder.GetMember(CSharpBinderFlags.None, name, typeof(JsonFile), new CSharpArgumentInfo[] { CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null) }));
                    return callsite.Target(callsite, obj);
                }

                return obj.GetFieldOrProperty(name);
            }
            catch(Exception ex)
            {
                throw new Exception("Cannot retrieve property '" + name + "' from object of type '" + (obj == null ? "null" : obj.GetType().FullName) + "': " + ex.Message, ex);
            }
        }
*/

        public static string ToMatrix<TRow, TColumn, TCell>(this IEnumerable<KeyValuePair<TRow, IEnumerable<KeyValuePair<TColumn, TCell>>>> rows)
        {
            return ToMatrix(rows.SelectMany(row => row.Value, (row, cell) => new { Row = row.Key, Cell = cell.Value, Col = cell.Key }), x => x.Row, x => x.Col, x => x.Cell);
        }

        public static string ToMatrix<T, TRow, TColumn, TCell>(this IEnumerable<T> items, Func<T, TRow> getRow, Func<T, TColumn> getColumn, Func<T, TCell> getCell)
        {
            var matrix = new Dictionary<TRow, Dictionary<TColumn, TCell>>();
            var allColumns = new HashSet<TColumn>();

            foreach (var item in items)
            {
                var row = getRow(item);
                var col = getColumn(item);
                var value = getCell(item);

                allColumns.Add(col);

                if (!matrix.ContainsKey(row)) matrix[row] = new Dictionary<TColumn, TCell>();

                matrix[row].Add(col, value);
            }

            var theColumns = allColumns.OrderBy(x => x).ToList();
            var theRows = matrix.Keys.OrderBy(x => x).ToList();

            var sb = ReseekableStringBuilder.AcquirePooledStringBuilder();

            foreach (var col in theColumns)
            {
                sb.Append('\t');
                sb.Append(col);
            }
            sb.AppendLine();
            foreach (var row in theRows)
            {
                sb.Append(row);
                var rowData = matrix[row];
                foreach (var col in theColumns)
                {
                    sb.Append('\t');
                    TCell value;
                    if (rowData.TryGetValue(col, out value))
                        sb.Append(RemoveForbiddenChars(value, '\t'));
                }
                sb.AppendLine();
            }
            return ReseekableStringBuilder.GetValueAndRelease(sb);
        }
        public static void ViewMatrix<T, TRow, TColumn, TCell>(this IEnumerable<T> items, Func<T, TRow> getRow, Func<T, TColumn> getColumn, Func<T, TCell> getCell)
        {
            Console.WriteLine(ToMatrix(items, getRow, getColumn, getCell));
        }
#if !CORECLR
        public static IEnumerable<T> CopyMatrix<T, TRow, TColumn, TCell>(this IEnumerable<T> items, Func<T, TRow> getRow, Func<T, TColumn> getColumn, Func<T, TCell> getCell)
        {
            CopyText(ToMatrix(items, getRow, getColumn, getCell));
            return items;
        }
        public static IEnumerable<T> CopyMatrix<T, TRow, TColumn, TCell>(this IEnumerable<T> items, Func<T, TRow> getRow, Func<T, TColumn> getColumn, Func<T, TCell> getCell, string caption)
        {
            CopyText(caption + "\n" + ToMatrix(items, getRow, getColumn, getCell));
            return items;
        }
#endif

        public static double RatioOf<T>(this IEnumerable<T> source, Func<T, bool> evalutor)
        {
            double sum = 0;
            double count = 0;
            foreach (var item in source)
            {
                if (evalutor(item))
                    sum++;
                count++;
            }
            return sum / count;
        }
        public static string RemoveForbiddenChars(object obj, char separator)
        {
            if (obj == null) return string.Empty;
            if (obj is DateTimeOffset datetimeoffset)
            {
                obj = new DateTime(datetimeoffset.UtcTicks, DateTimeKind.Utc);
            }
            if (obj is DateTime d)
            {
                if (d.Ticks < TimeSpan.FromDays(2).Ticks) return string.Empty;
                if (d.TimeOfDay == TimeSpan.Zero) obj = d.ToString("yyyy-MM-dd");
                else obj = d.ToString("yyyy-MM-dd HH:mm:ss");
            }

            if (obj is System.Collections.ICollection col)
            {
                if (col.Count == 0) return string.Empty;
                var sb = ReseekableStringBuilder.AcquirePooledStringBuilder();
                var first = true;
                foreach (var item in col)
                {
                    if (!first) sb.Append(",");
                    sb.Append(RemoveForbiddenChars(item, '\0'));
                    first = false;
                }
                obj = ReseekableStringBuilder.GetValueAndRelease(sb);
            }
            string str;
#if STANDALONE
            if (obj is double)
            {
                str = ((double)obj).ToString("0.000");
            }
            else if (obj is float)
            {
                str = ((float)obj).ToString("0.000");
            }
            else if (obj is decimal)
            {
                str = ((decimal)obj).ToString("0.000");
            }
            else
            { 
                str = Convert.ToString(obj, CultureInfo.InvariantCulture);
            }
#else
            str = Conversions.ConvertToDisplayString(obj);
#endif
            //var str = Convert.ToString(obj, CultureInfo.InvariantCulture);
            if (str == null) return string.Empty;
            var k = str.Replace("\r", "").Replace('\n', ' ');
            if(separator != OUTPUT_ALIGNED) k = k.Replace(separator, ' ');
            return k;
        }



        public static Type CreateAnonymousType(IEnumerable<string> fieldNames)
        {
            return CreateAnonymousType(fieldNames.Select(x => new KeyValuePair<string, Type>(x, typeof(object))));
        }


        public static Type CreateAnonymousType(IEnumerable<KeyValuePair<string, Type>> fields)
        {
#if STANDALONE
            return DynamicAnonymousType.CreateAnonymousType(fields);
#else
            return Querylang.QType.CreateAnonymousType(fieldNames.Select(x => new KeyValuePair<string, Type>(x, typeof(object)))).clrType;
#endif
        }

        public static IEnumerable<object> CreateAnonymousEnumerable<T>(IEnumerable<T> items, IReadOnlyList<(string name, Func<T, object> retriever)> fields, out Type elementType)
        {
            var t = CreateAnonymousType(fields.Select(x => x.name));
            elementType = t;

            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericTypeFast(t));

            var setters = fields.Select(x => ReflectionHelper.GetSetter<object, object>(t.GetField(x.name))).ToList();


            var z = typeof(AnonymousEnumerable<,>).MakeGenericTypeFast(typeof(T), elementType);
            var enumerable = (AnonymousEnumerableBase<T>)Activator.CreateInstance(z);
            enumerable.source = items;
            enumerable.setters = setters;
            enumerable.fields = fields;
            return (IEnumerable<object>)enumerable;
        }


        internal class AnonymousEnumerableBase<T>
        {
            internal IEnumerable<T> source;
            internal List<Action<object, object>> setters;
            internal IReadOnlyList<(string name, Func<T, object> retriever)> fields;
        }

        internal class AnonymousEnumerable<TSource, TResult> : AnonymousEnumerableBase<TSource>, IEnumerable<TResult> where TResult : new()
        {
            public IEnumerator<TResult> GetEnumerator()
            {
                return new AnonymousEnumerator<TSource, TResult>(this);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return new AnonymousEnumerator<TSource, TResult>(this);
            }

        }


        internal class AnonymousEnumerator<TSource, TResult> : IEnumerator<TResult> where TResult : new()
        {
            private AnonymousEnumerable<TSource, TResult> anonymousEnumerable;
            private IEnumerator<TSource> source;
            private TResult current;

            internal AnonymousEnumerator(AnonymousEnumerable<TSource, TResult> anonymousEnumerable)
            {
                this.anonymousEnumerable = anonymousEnumerable;
                this.source = anonymousEnumerable.source.GetEnumerator();
            }

            public TResult Current => current;

            object IEnumerator.Current => current;

            public void Dispose()
            {
                this.source.Dispose();
            }

            public bool MoveNext()
            {
                if (!source.MoveNext()) return false;
                var item = source.Current;
                var row = new TResult();
                var fieldIdx = 0;
                var setters = anonymousEnumerable.setters;
                foreach (var field in anonymousEnumerable.fields)
                {
                    var val = field.retriever(item);
                    setters[fieldIdx](row, val);
                    fieldIdx++;
                }
                current = row;
                return true;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }
        }


    }



    public abstract class OutputEmitter : IDisposable
    {



        [Configuration]
        public static int Configuration_BatchSize = 10;
        protected IReadOnlyList<(string name, Func<object, object> retriever)> fields;
        protected Func<object> ctor;
        protected List<Action<object, object>> setters;

        public abstract void Flush();
        public abstract void Dispose();
        public abstract void Emit(object obj);

        public static OutputEmitter Create(string outfile, Type elementType)
        {
            return (OutputEmitter)Activator.CreateInstance(typeof(OutputEmitter<>).MakeGenericTypeFast(elementType), new object[] { outfile });
        }

        internal static OutputEmitter Create<TSource>(string outfile, IReadOnlyList<(string name, Func<TSource, object> retriever)> fields)
        {
            if (fields.Select(x => x.name).Distinct().Count() != fields.Count)
            {
                var f = new List<(string name, Func<TSource, object> retriever)>();

                foreach (var field_ in fields)
                {
                    var field = field_;
                    var cn = field.name;
                    var i = 2;
                    while (f.Any(x => x.name == cn))
                    {
                        cn = field.name + i;
                        i++;
                    }
                    field.name = cn;
                    f.Add(field);
                }

                fields = f;
            }
            var elemType = ReplExtensions.CreateAnonymousType(fields.Select(x => x.name));

            var z = Create(outfile, elemType);
            z.fields = fields.Select(x=>(x.name, new Func<object, object>(y=>x.retriever((TSource)y)))).ToList();
            z.ctor = ReflectionHelper.GetWrapper<Func<object>>(elemType, ".ctor");
            z.setters = fields.Select(x => ReflectionHelper.GetSetter<object, object>(elemType.GetField(x.name))).ToList();
            return z;
        }
    }

    public class OutputEmitter<T> : OutputEmitter
    {
        private string outfile;

        private List<T> batch = new List<T>();
        private bool mustCollectAll;

        private ReplExtensions.ContinuationInfo info;

        public OutputEmitter(string outfile)
        {
            outfile = outfile ?? "CON:.txt-preformatted";
            this.outfile = outfile;
            this.info = new ReplExtensions.ContinuationInfo();
            mustCollectAll = outfile.EndsWith(".xlsx") || outfile.EndsWith(".json") || outfile.StartsWith("CLIPBOARD:");
        }

        public void Emit(T obj)
        {
            batch.Add(obj);
            if (!mustCollectAll && batch.Count >= Configuration_BatchSize) Flush();
        }

        public override void Dispose()
        {
            if (mustCollectAll)
            {
                batch.SaveTable(outfile);
            }
            else
            { 
                Flush();
            }
            batch = null;
        }

        public override void Flush()
        {
            if (mustCollectAll) return;
            batch.SaveTable(outfile, ref info);
            batch.Clear();
        }

        public override void Emit(object obj)
        {
            if (fields != null && obj != null)
            {
                var k = ctor();
                for (int i = 0; i < fields.Count; i++)
                {
                    
                    var v = fields[i].retriever(obj);
                    setters[i](k, v);
                }
                obj = (T)k;
            }
            Emit((T)obj);
        }
    }


}
