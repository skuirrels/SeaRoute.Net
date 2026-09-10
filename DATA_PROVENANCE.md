# Embedded data provenance

This file records the exact inputs and transformations used for the binary resources in `src/SeaRoute/Data`.
SHA-256 values are hashes of the files as stored, not of their decompressed JSON.

## Maritime network and ports

- Distribution: [genthalili/searoute-py 1.6.0](https://github.com/genthalili/searoute-py/tree/1.6.0), commit `dde461382a60ff75f4577f6e06c411bc48cd7efc`.
- Source `marnet_searoute.geojson`: SHA-256 `111a82d949bb949c396a69a08828781314d3afe779c07e9754c3b2834dfca415`.
- Source `segment.geojson`: SHA-256 `56052b2ddd35de483ec00a29a7f0ffc37e162b0ee0249a9a435c8a8b2d8ffe0f`.
- Source `ports.geojson`: SHA-256 `8398cb9554a19cf37f53f932f3842f58fbd94fce4c8e2704c3f5a4205ea5977e`.
- Embedded `marnet.json.gz`: SHA-256 `8d9be0fb7881b77f46d21bd066779cd4c561f8f70c80f02a5e381706ff7aa880`; 9,708 nodes and 31,950 directed edges.
- Embedded `ports.json.gz`: SHA-256 `ccf97018a8d8c9cccafc08d69388dc0b483e39520f24bde94b36d6a8f8d2dca2`; 3,962 records.

`tools/build-marnet-data.py` combines the tagged Marnet network with its tagged antimeridian segments, converts every line to directed edges in both directions, and calculates weights with the same mean-earth-radius Haversine formula used by the library. `tools/build-ports-data.py` retains every source port record, including duplicate codes. Both scripts verify their input hashes and write deterministic gzip files.

The searoute-py project credits [Eurostat SeaRoute](https://github.com/eurostat/searoute) as the origin of the maritime network. Eurostat describes that network as based on the Oak Ridge National Labs CTA global shipping lane network, enriched around European coasts with AIS-derived lines.

## UN/LOCODE

- Publisher: [United Nations Economic Commission for Europe, UN/LOCODE](https://unlocode.unece.org/).
- Embedded `unlocode.json.gz`: SHA-256 `6e39b6efc925d804af5b304a4db2f701782cc4226fe31e2b97cd3b0bccfb052a`; 106,588 codes, of which 84,516 contain publisher coordinates.
- Embedded `unlocode-supplement.json`: SHA-256 `6ce12ca501948e6f8c5e962fe901673627e2d05a9e1a666118896261a2437716`; four attributed coordinate supplements, applied only when the UN/LOCODE row has no coordinate.

Important limitation: the imported UN/LOCODE resource did not retain an edition or source-file checksum, and repository history does not identify one. It must therefore not be described as the current official edition. The [UNECE publications page](https://unlocode.unece.org/publications/) is the authority for the current production and pre-release datasets. A future refresh should replace this resource from a named publication and record its source checksum here.

## Licences and attribution

See `THIRD-PARTY-NOTICES.md`. Data licences are separate from the Apache-2.0 licence covering SeaRoute.Net's own code.
