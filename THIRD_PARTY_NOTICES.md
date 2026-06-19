# Third-Party Notices

NetAccel Windows is derived from v2rayN and remains licensed under GPL-3.0.
The source distribution retains the upstream copyright and license files.

Release builds must run `scripts/plan16/generate-sbom.ps1`. The resulting SPDX
2.3 document is the authoritative package inventory for that build and must be
published beside the installer. NuGet package license conclusions remain
`NOASSERTION` until reviewed against the generated SBOM; they must not be
silently inferred by the build.

Xray and sing-box are separately distributed components. Their exact versions,
hashes, signatures, source links, and license notices must be pinned in the
signed release metadata used for a NetAccel release.
