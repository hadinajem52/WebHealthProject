# oxipng runtime

The PNG audit uses the official oxipng 10.2.0 release with `-o max --strip safe`. It deliberately
does not use `--alpha`, because changing hidden RGB values would violate exact RGBA verification.

Pinned executable SHA-256 values:

- `win-x64/oxipng.exe`: `394FEF4CCBC6EE5A50BA96FE75AF3557C4365349EB80371EF9CCC76F903C2530`
- `linux-x64/oxipng`: `A2C08BF8F9914A03FC8A1D74E17E2F34A6BAC3616126627A5DD824DA5D6529E7`

The upstream license is included in `LICENSE.txt`.
