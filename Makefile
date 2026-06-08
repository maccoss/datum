.PHONY: check build test run clean

check:
	dotnet format Datum.sln --exclude src/Datum.Skyline/Vendor/
	dotnet build Datum.sln -warnaserror

build: check
	dotnet build Datum.sln -c Release

test: check
	dotnet test Datum.sln

run:
	dotnet run --project src/Datum.App

clean:
	dotnet clean Datum.sln
