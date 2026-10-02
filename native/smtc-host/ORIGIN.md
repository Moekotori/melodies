# SMTC helper source

Imported read-only from ECHO commit `3dbf28993cddf9f10ea355b88b0105abe85e5174` on 2026-10-02.
`src/main.cpp` and the RC template retain the existing implementation; the CMake icon path points to the local copy.
The 717-line imported C++ file is retained as upstream source, rather than rewritten during the C# migration.

The staged Windows x64 helper comes from that workspace's existing build. Its source-to-binary correspondence
has not been established by rebuilding this round. SHA256:
`6936A61DD270C38B3AD9E2581846F0F937D35FEDBBD9208C2C639BE4291A5B3E`.
The adapter requires protocol 1 and verifies advertised capabilities before publishing.
Imported `src/main.cpp` SHA256: `D688F79A687154A47EF5CBDD921F10E1F1CA86D1F299A67B1F6185CBAB4C1084`.

Rebuild locally with CMake / Visual Studio 2022 / Windows SDK:
`cmake -S native/smtc-host -B misc/smtc-build -G "Visual Studio 17 2022" -A x64`
then `cmake --build misc/smtc-build --config Release --parallel 2`.
Copy only the resulting `echo-smtc-host.exe` to `native/runtime/win-x64/`.
Observe the workspace's 60-second command and 120-second validation limits.
The old workspace remains the rollback application and is never modified by this integration.
