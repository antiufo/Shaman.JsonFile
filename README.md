# Shaman.JsonFile

Quickly (de)serialize data to/from the disk, also useful for debugging/development.

# Usage
```csharp
using (var json = JsonFile.OpenList<Something>("Example.json"))
{
    json.Content.Add(something);
}
```
Notes:
* Safe for recursion/reentrancy: if you recursively open the same file, the same instance will be used. The file be saved when the outermost instance ends.
* The file is saved automatically and atomically when the end of `using` is reached. To discard the result, use `json.DiscardAll()`.
* If you want to periodically save the file eg. when processing large amounts of data, you can use `IncrementChangeCountAndMaybeSave()`. The file will be saved every `MaximumUncommittedChanges` calls to this method (configurable).
* If your path ends with `.pb`, Protocol Buffers will be used instead of JSON for serializing the data (remember to add `[ProtoContract]` and `[ProtoMember]` in these cases).

# ReplExtensions
```csharp
something
    .Where(x => …)
    .Select(x => new { FirstName = …, LastName = …, BirthDate = … })

    .ViewTable(); // Prints the data to the console as a table
    .ViewJson(); // Prints the data to the console as JSON
    .ToExcel(); // Saves an .xlsx file and launches it
    
    .CopyTable(); // Copies the data to the clipboard as a table (.NET Framework only)
    .CopyJson(); // Copies the data to the clipboard as JSON (.NET Framework only)

    .ViewMatrix(x => rowSelector, x => columnSelector, x => cellSelector); // Prints a 2D matrix
```