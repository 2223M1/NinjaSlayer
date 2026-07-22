# Private contract harness

This project is the trusted build entry used by the protected game-contract workflow. It compiles candidate C# sources against private game references without evaluating the candidate repository's project, targets, NuGet configuration, tests, or workflows.

Changes to dependencies or top-level source directories must update this harness on protected `main` before a later candidate can consume them. The harness never packages or runs candidate code and its output is deleted at the end of every workflow run.
