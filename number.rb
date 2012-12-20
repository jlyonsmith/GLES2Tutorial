root = 'OpenGLES'
matches = /.*?(\d\d)/.match(Dir::pwd)
num = matches[1]
name = root + '_' + num

sln = Dir["*.sln"].first
csproj = Dir["*.csproj"].first

contents = File::read(sln)
contents = contents.gsub(/#{root}_\d\d/, name)
File::delete(sln)
sln = name + '.sln'
File::write(sln, contents)

contents = File::read(csproj)
contents =  contents.gsub(/#{root}_\d\d/, name)
File::delete(csproj)
csproj = name + '.csproj'
File::write(csproj, contents)