set fallback := true

add-license-cs:
    go install github.com/fbiville/headache/cmd/headache@latest
    headache --configuration ./configuration-cs.json
add-migration name:
    cd src && dotnet ef migrations add {{name}}
remove-migration:
    cd src && dotnet ef migrations remove
drop-db:
    cd src && dotnet ef database drop -f
  