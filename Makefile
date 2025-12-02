run:
	dotnet build && qemu-system-x86_64   -cdrom bin/cosmos/Debug/net6.0/testOS.iso   -drive file=mydisk.img,format=raw,if=ide -drive file=mydisk2.img,format=raw,if=ide
build:
	dotnet build
