﻿namespace Revit.TransactionMemoryCache.Analyzers.Tests;

/// <summary>Source shared by every analyzer/code-fix test: a Revit-API-free stand-in for CachedElementCollector's public surface.</summary>
internal static class TestStubs
{
    public const string CachedElementCollectorStub = """
        namespace Revit.TransactionMemoryCache.Services
        {
            public sealed class CachedElementCollector
            {
                public CachedElementCollector OfClass(System.Type elementClass) => this;
                public CachedElementCollector Of<TElement>() => this;
                public CachedElementCollector OfCategory(int category) => this;
                public CachedElementCollector OfCategories(System.Collections.Generic.IEnumerable<int> categories) => this;
                public CachedElementCollector WhereParameterEquals(int parameter, int value) => this;
                public CachedElementCollector WhereElementIsElementType() => this;
                public CachedElementCollector WhereElementIsNotElementType() => this;
                public CachedElementCollector Excluding(System.Collections.Generic.ICollection<int> elementIds) => this;
                public System.Collections.Generic.IReadOnlyList<object> ToElements() => null!;
                public System.Collections.Generic.IReadOnlyList<int> ToElementIds() => null!;
            }
        }
        """;
}
