# Persistence repositories

Place concrete repository implementations here when database-backed use cases are added. Repository contracts belong in Application and should be introduced only for real persistence boundaries.

Repositories may use the generated EF Core context and models, but must return Domain entities or Application projections instead of leaking generated database models outside Infrastructure.
