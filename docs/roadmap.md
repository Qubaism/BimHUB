# BimHUB Roadmap

BimHUB will be developed in small, reviewable stages. Priorities may change as workflows are tested and new requirements are identified.

## Current Development

The current development build provides the main product foundation:

- Revit add-in projects for .NET Framework 4.8 and .NET 8
- Version-specific builds for Revit 2023 through Revit 2026
- QBTools ribbon panel and BimHUB command
- Dockable library panel
- Multiple family and details library locations
- Search, category filters, list view, and grid view
- Family previews, metadata, available types, favorites, and recent items
- Family loading by double-click and drag and drop
- Drafting-view and drafting-sheet catalogs
- Drafting-view and drafting-sheet import
- Background family thumbnail and details preview generation
- Local catalog caching for faster startup and navigation

## Compatibility Validation

- Hands-on workflow testing completed in Revit 2023
- Hands-on workflow testing completed in Revit 2024
- Hands-on workflow testing completed in Revit 2025
- Hands-on workflow testing completed in Revit 2026
- Continue cross-version regression and stability testing
- Build and test Revit 2027 after its matching Revit API is available locally
- Confirm family, drafting-view, and drafting-sheet behavior in every supported version

## Product Stabilization

- Test large local and shared network libraries
- Improve error reporting for unavailable or incompatible content
- Validate background indexing and cancellation during long operations
- Test duplicate families, types, views, and sheet numbers
- Confirm settings, favorites, and recent items survive Revit restarts
- Expand automated tests around catalog, filtering, cache, and import preparation logic

## First Public Release

- Define the first release feature set
- Prepare a signed installer
- Add installation, update, and removal guidance
- Complete final regression testing
- Publish a versioned GitHub release

## Future Possibilities

- Additional library metadata and custom tags
- Team-managed library standards
- Content usage information
- More BIM content formats
- Optional centralized catalog management
