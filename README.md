# Reflux

[![Version](https://img.shields.io/nuget/v/Reflux.svg?style=flat-square)](https://www.nuget.org/packages/Reflux/)

A simple Harmony-like patching library using [MonoMod.RuntimeDetour](https://github.com/MonoMod/MonoMod).

A goal of this project is to almost mimic how Harmony patches the methods, though some features might not be added due to
lack of interest with it.

## Roadmap
+ [x] Prefixes
+ [x] Postfixes
+ [x] Finalizers
+ [ ] Reverse Patchers

## Things will not do
+ [ ] Transpilers

Some additional credits:
+ [CelesteMappingUtils](https://github.com/JaThePlayer/CelesteMappingUtils) - On MethodDiff for IL dumping.