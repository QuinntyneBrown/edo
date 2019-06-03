pipeline {
    agent { label 'master' }

    environment {
        URL_REGISTRY = 'registry.dev.happytravel.com'
        APP_NAME = 'edo-api'
        IMAGE_NAME = 'edo-api:dev'
        NAMESPACE = 'dev'
    }

    stages {
        stage('Checkout happytravel repository') {
            steps {
                dir('docker/edo-api') {
                    git branch: 'master', credentialsId: 'bitbucket', url: 'git@bitbucket.org:happytravel/edo-api.git'
                }
            }
        }
        stage('Force login at docker registry') {
            steps {
                sh 'docker login https://$URL_REGISTRY -u username -p password'
            }
        }
        stage('Build docker image') {
            steps {
                dir('docker/edo-api') {
                    sh 'docker build -t $URL_REGISTRY/$IMAGE_NAME-$BUILD_NUMBER . --no-cache'
                }
            }
        }
        stage('Push docker image to repository') {
            steps {
                sh 'docker push $URL_REGISTRY/$IMAGE_NAME-$BUILD_NUMBER'
                sh 'docker tag $URL_REGISTRY/$IMAGE_NAME-$BUILD_NUMBER $URL_REGISTRY/$IMAGE_NAME-latest'
                sh 'docker push $URL_REGISTRY/$IMAGE_NAME-latest'                
            }
        }
        stage('Deploy to k8s') {
            steps {
                dir('docker/edo-api/Helm') {
                    withCredentials([file(credentialsId: 'k8s', variable: 'k8s_cred')]) {
                        sh './setRevision.sh $BUILD_NUMBER'
                        sh 'helm --kubeconfig /$k8s_cred upgrade --install $APP_NAME --wait --namespace $NAMESPACE ./'
                    }
                }
            }
        }        
    }
    post {
        always {
            echo 'One way or another, I have finished'
            deleteDir() /* clean up our workspace */
        }
        success {
            echo 'I succeeeded!'
        }
        unstable {
            echo 'I am unstable :/'
        }
        failure {
            echo 'I failed :('
        }
        changed {
            echo 'Things were different before...'
        }
    }
}