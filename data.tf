data "aws_caller_identity" "current" {}
data "aws_region" "current" {}

resource "null_resource" "build_dotnet_project" {
  provisioner "local-exec" {
    command = "dotnet publish -c Release -r linux-arm64 --self-contained false"
    
    # aot native image instead of bytecode
    #command = "dotnet publish -r linux-arm64 -c Release -p:StripSymbols=true -p:CppCompilerAndLinker=clang-9 -p:SysRoot=/cross_sysroot -p:LinkerFlavor=lld --self-contained false"
    
  }

  triggers = {
    always_run = "${timestamp()}"
  }
}

data "archive_file" "chat_listener" {
  type        = "zip"
  source_dir  = "chat_listener/bin/Release/net8.0/linux-arm64/publish"
  output_path = "chat_listener.zip"

  depends_on = [null_resource.build_dotnet_project]
}

