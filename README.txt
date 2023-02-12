SqlInvoke
=========
This is not an ORM, but a "foreign language interface" towards SQLServer using ADO.NET and `Microsoft.Data.SqlClient`.
The name of the project, which plays on P/Invoke, reflects its underlying philosophy.

I'd like to thank my employer, Quine AS (which I'm a co-founder of), for allowing me to open-source this project.

Not an ORM?
-----------
No.  The library takes care only about mapping between C# classes and SQL command parameters / result set records.
However, it _does_ come with some directly usable features:

- CRUD statement builders for entities (i.e., tables with a primary key),
- Result set readers,
- SqlRowAccessor<T> class that helps to reduce boilerplate in the most common case of executing a fixed-text statement

To get acquainted with the library, start with `Models.cs` and `TestSqlContext.cs` in the test project.  Then read
the test cases to see the library in use.  NOTE: `SqlContext` class is the entry point to all functionality.  Even
though the name is similar, the functionality has nothing in common with EFCore contexts.  (`SqlContext` is a cache
/ reflection / compilation context for expression trees that transfer data items between C# objects and SQL row and
parameter sets.)

Why not EFCore?
---------------
I have a number of issues with EFCore.  In a nutshell: it dumbs a marvel of advanced engineering (transactional SQL
database with procedural language) down to a stupid key-value object store.  Read further for more details.

First, navigational properties make a messy data model and a messy application.  The component receiving an entity
instance must know where the instance came from in order to reliably use its navigational properties.  In general,
there's no nice mapping between flat result sets and object graphs.  (Yes, DTOs.  Code duplication squared.)

Second, it doesn't play nice with transactions.  Want a transactional read-modify-write operation on one or more rows?
Well, go and construct your own SQL string containing the `(XLOCK, ROWLOCK)` hint.  (You can attempt this without a hint,
but be prepared to handle the "deadlock victim" exception.)

Third, you have no control over the resulting SQL.  For a fully custom query, you have to define a separate entity
class anyway, just like with this library.  True, executing a literal SQL statement is easier (less boilerplate) than
with this library, but the downside is hunting them down after a schema change. (In my projects, I use stored procedures,
with raw SQL statements fetched from resources.  That way, they're easy to find and audit.)

Fourth, combinatorial explosion on joins (though this might be addressed in EFCore7 with split queries.)  This library
allows you to execute multiple SELECT statements as a single command (batch) and read multiple result sets.

Fifth, you either select too much data or use projections and end up with anonymous types.

Sixth, unlike this library, it doesn't support records.  This makes caching and sharing mutable entity instances
a rather tricky proposition in a concurrent setting.  (Yes, I've done it, in a rather sickening way: strict interface
for cache lookups combined with serialization to deep-clone entity instances before giving them out.)

Why not Dapper?
---------------
Weakly-typed: too much reliance on anonymous objects, dynamic, and ad-hoc embedded SQL statement strings with implicit
conversions between C# objects and SQL server types.  It also does not support custom column converters. I believe that
interfacing with a database should be as well thought-out as other aspects of application design.  This library is a
toolkit that forces you to first define structued "SQL methods", but, in exchange, lets you later invoke them easily,
without all the ceremony of ADO.NET.

Why not X?
----------
Too many "micro-orms" to investigate.  Instead of trying to find one and "squaring a circle" with it, I sat down and wrote
one that I myself would like to use in the long run.

Support for other databases?
----------------------------
Sorry, out of luck.  In Quine, we use exclusively MS SQLServer and that's what the library supports.  Requests for
supporting other databases will be outright rejected - UNLESS - you also do the architectural work required to select
a database driver at run-time and/or compile-time.  On the other hand, the project is open-source, so you're welcome
to fork it.

TODOs
-----
Minor: add tests for computed columns being ignored by insert/update statement builders.

Minor: support OUTPUT clause in insert/update/delete statement builders.  This will allow for elegant fetching of
auto-generated keys and complete state of updated/deleted entities without a round-trip to the databse.

Major: This library is a perfect use-case for abstract static methods in interfaces.

Major: Importing an existing in-memory EFcore model and automatically building row accessors for all declared entities.
