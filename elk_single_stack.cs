# Cloudscript for an Elasticsearch + Logstash + Kibana server
cloudscript elk_single_stack
    version                     = '2014_12_08'
    result_template             = elk_result_template

globals
    elk_hostname                = 'elk-test'
    elk_instance_type           = 'CS2.1' # 2Gb, 1 core, 75Gb, 1Gbps
    elk_image_type              = 'Linux CentOS 6.5 64-bit'
    elk_slice_user              = 'elk'
    # http://www.elasticsearch.org/overview/elkdownloads/
    elasticsearch_version       = 'elasticsearch-1.4.1'
    logstash_version            = 'logstash-1.4.2'
    kibana_version              = 'kibana-3.1.0'
    # Password setup
    server_password             = lib::random_password()
    console_password            = lib::random_password()

thread elk_setup
    tasks                       = [elk_server_setup]

task elk_server_setup

    #
    # Create elk keys
    #

    # Create elk server root password key
    /key/password elk_server_password_key read_or_create
        key_group               = _SERVER
        password                = server_password

    # Create elk server console key

    /key/password elk_server_console_key read_or_create
        key_group               = _CONSOLE
        password                = console_password

    #
    # Create elk storage slice, bootstrap script and recipe
    #

    # Create storage slice keys
    /key/token elk_slice_key read_or_create
        username                = elk_slice_user

    # Create slice to store script in cloudstorage

    /storage/slice elk_slice read_or_create
        keys                    = [elk_slice_key]

    # Create slice container to store script in cloudstorage

    /storage/container elk_container => [elk_slice] read_or_create
        slice                   = elk_slice

    # Place script data in cloudstorage

    /storage/object elk_bootstrap_object => [elk_slice, elk_container] read_or_create
        container_name          = 'elk_container'
        file_name               = 'bootstrap_elk.sh'
        slice                   = elk_slice
        content_data            = elk_bootstrap_data

    # Associate the cloudstorage object with the elk script

    /orchestration/script elk_bootstrap_script => [elk_slice, elk_container, elk_bootstrap_object] read_or_create
        data_uri                = 'cloudstorage://elk_slice/elk_container/bootstrap_elk.sh'
        script_type             = _SHELL
        encoding                = _STORAGE

    # Create the recipe and associate the script

    /orchestration/recipe elk_bootstrap_recipe read_or_create
        scripts                 = [elk_bootstrap_script]

    #
    # Create the elk server
    #

    /server/cloud elk_server read_or_create
        hostname                = '{{ elk_hostname }}'
        image                   = '{{ elk_image_type }}'
        service_type            = '{{ elk_instance_type }}'
        keys                    = [elk_server_password_key, elk_server_console_key]
        recipes                 = [elk_bootstrap_recipe]

text_template elk_bootstrap_data
#!/bin/sh

# check if running as root
if [ "$EUID" -ne 0 ]
    then echo "ERROR: must have root permissions to execute the commands"
    exit
fi

#
# Install packages
#

# Get latest package list
yum --quiet --assumeyes update

# Install wget
yum --quiet --assumeyes install wget

# Install iptable services
yum --quiet --assumeyes install iptables-services

# Install httpd
yum --quiet --assumeyes install httpd

# Install and setup Java
yum --quiet --assumeyes install java
export JRE_HOME=/usr/lib/jvm/jre
export PATH=$PATH:/usr/lib/jvm/jre/bin

# Install and setup Elasticsearch
cd /opt/
wget https://download.elasticsearch.org/elasticsearch/elasticsearch/{{ elasticsearch_version }}.tar.gz
tar -xf {{ elasticsearch_version }}.tar.gz
ln -s /opt/{{ elasticsearch_version }} /opt/elasticsearch

# Modify Elasticsearch config
echo "http.cors.enabled: true" >> /opt/elasticsearch/config/elasticsearch.yml

# Start up Elasticsearch in the background
/opt/elasticsearch/bin/elasticsearch -d

# Install and setup Logstash
cd /opt/
wget https://download.elasticsearch.org/logstash/logstash/{{ logstash_version }}.tar.gz
tar -xf {{ logstash_version }}.tar.gz
ln -s /opt/{{ logstash_version }} /opt/logstash

# Prepare a logstash.conf file for Apache logs
cat <<\EOF>/opt/logstash/logstash.conf
input {
  file {
    path => "/tmp/access_log"
    start_position => beginning
    sincedb_path => "/opt/logstash/sincedb.log"
  }
}
filter {
  if [path] =~ "access" {
    mutate { replace => { "type" => "apache_access" } }
    grok {
      match => { "message" => "%{COMBINEDAPACHELOG}" }
    }
  }
  date {
    match => [ "timestamp" , "dd/MMM/yyyy:HH:mm:ss Z" ]
  }
}
output {
  elasticsearch {
    host => localhost
  }
  stdout { codec => rubydebug }
}
EOF

# Start logstash
nohup /bin/bash /opt/logstash/bin/logstash agent -f /opt/logstash/logstash.conf -l /opt/logstash/logstash.log > /dev/null 2>&1 &

# Install and setup Kibana
cd /opt/
wget https://download.elasticsearch.org/kibana/kibana/{{ kibana_version }}.tar.gz
tar -xf {{ kibana_version }}.tar.gz

#  Copy the contents of the extracted directory to your webserver root directory
cp -R /opt/{{ kibana_version }}/* /var/www/html
chown -R apache:apache /var/www/html

# change kibana conf 
sed -r 's@ elasticsearch:.*@ elasticsearch: "http://"+window.location.hostname+":9200",@g' /var/www/html/config.js  > /var/www/html/config_new.js
mv /var/www/html/config_new.js /var/www/html/config.js

# add firewall rules and restart iptables
NUM = iptables -L INPUT --line-numbers | tail -n1 |cut -c 1
iptables -I INPUT $NUM -m state --state NEW -m tcp -p tcp --dport 80 -j ACCEPT
iptables -I INPUT $NUM -m state --state NEW -m tcp -p tcp --dport 9200 -j ACCEPT
service iptables save
service iptables restart

# start httpd
chkconfig --level 2345 httpd on
service httpd start

#i autostart elasticsearch
echo '/opt/elasticsearch/bin/elasticsearch -d' >> /etc/rc.local.orig
echo '/bin/bash /opt/logstash/bin/logstash agent -f /opt/logstash/logstash.conf -l /opt/logstash/logstash.log &' >> /etc/rc.local.orig
chmod +x /etc/rc.local.orig

_eof

text_template elk_result_template

Your Elasticsearch + Logstash + Kibana is ready at the following IP address:

http://{{ elk_server.ipaddress_public }}/

Go to your server address and read the Kibana dashboard for further instructions.

_eof
