# PostgreSQL protocol tests

Run `npm ci` and `npm test` in this folder. PGlite runs actual PostgreSQL locally,
without Docker, credentials, network calls or changes to the Supabase project.
The SQL stays compatible with the project's PostgreSQL 17 despite PGlite using 18.

The tests apply the relational migrations, seed three old development favorites,
and exercise RLS, replay, ABA, duplicate timestamps, lost acknowledgements,
recursive deletion, bounded history and snapshot fallback.

`parity-fixture.json` is deterministic test data exported from PostgreSQL. Commit
it when the protocol changes. The .NET regression project consumes it to verify
identical canonical digests, fractional rank allocation and JSON decoding.
No real account or song data belongs in this fixture.
