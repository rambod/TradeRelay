TradeRelay 1.0.0 — Linux portable build

Run ./launch-traderelay from this directory. The archive installs nothing automatically.

Primary support: Ubuntu 24.04 on X11 or XWayland. Required system libraries include
fontconfig, libX11, libICE, libSM, libXext, and libXrender. Protected credential
persistence additionally uses secret-tool from libsecret-tools. On Ubuntu:

  sudo apt install fontconfig libx11-6 libice6 libsm6 libxext6 libxrender1 libsecret-tools

The application stores settings and protected-data integrations below
~/.config/TradeRelay. Verify the archive with the published SHA256SUMS before use.
