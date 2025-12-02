run:
	dotnet build && qemu-system-x86_64   -cdrom bin/cosmos/Debug/net6.0/gotailsos.iso   -drive file=mydisk.img,format=raw,if=ide -drive file=mydisk2.img,format=raw,if=ide -boot order=d
build:
	dotnet build
