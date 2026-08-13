# Persistence mappers

Place mappings between `Generated/Models` and Domain entities or Application projections here.

Generated EF Core models must stay persistence-only. Do not add mapping code inside generated files because a later scaffold can overwrite them.
