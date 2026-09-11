# OffensiveFSharp
A repository thats based on the tooling avaliable in the Offensive C# repository but made in the F# language instead

### Why F#?

The dotnet framework is incredibly powerful, and as a result the C# language has become a favorite of many. The issue is that C# is a OOP language that's a nudge above say python, a language a lot more security inclined folks are comfortable with. F# as a result helps bring dotnet power to a language that looks closer to python than its sibling C#


### Work in progress

This repository is mainly an excuse for me to learn F# as a more accessible language to explore dotnet functionality as well as a place to share how some known C# tooling looks like when translated into F# less it may encourage people to try it out!

This repository is meant for educational purposes only, most things to be featured here are PoCs on what F# can do easy and quick but do explore it on your own and share with others what you build!

### ETWEventSubscription

`ETWEventSubscription` observes successful user logons or process starts through Event Tracing for Windows. Run it from an elevated console and press `Ctrl+C` to stop the trace session.

```powershell
ETWEventSubscription.exe -UserLogon
ETWEventSubscription.exe -ProcStart powersh
```
