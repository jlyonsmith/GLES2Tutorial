# Remove .userprefs
Dir["*.userprefs"].each { |fileName | File.delete(fileName) }
system("rm -rf bin")
system("rm -rf obj")

