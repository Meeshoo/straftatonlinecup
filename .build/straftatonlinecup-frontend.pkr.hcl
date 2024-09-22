source "docker" "nginx" {
  image = "nginx"
  commit = true
  changes = [
    "ENV FOO bar",
    
  ]
}

build {
  sources = ["source.docker.nginx"]

  provisioner "file" {
    source = "../straftatonlinecup-frontend/"
    destination = "/usr/share/nginx/html"
  }

  post-processors {
    post-processor "docker-tag" {
      repository = "550661752655.dkr.ecr.eu-west-1.amazonaws.com/straftatonlinecup-frontend"
      tags       = ["latest"]
    }

    post-processor "docker-push" {
      ecr_login = true
      login_server = "https://550661752655.dkr.ecr.eu-west-1.amazonaws.com/mitlan"
    }
  }
}
