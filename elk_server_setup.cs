# Cloudscript for an Elasticsearch + Logstash + Kibana server
cloudscript elk_single_stack
    version                     = '2014_09_18'
    result_template             = elk_result_template

globals
    # !!! Set instance variables !!!
    elk_hostname                = 'elk-test'
    elk_instance_type           = 'CS2.1' # 2Gb, 1 core, 75Gb, 1Gbps
    elk_image_type              = 'Linux CentOS 6.5 64-bit'
    elk_slice_user              = 'elk'
    # !!! Set elk version variables to latest versions        !!!
    # !!! http://www.elasticsearch.org/overview/elkdownloads/ !!!
    elasticsearch_version       = 'elasticsearch-1.3.2'
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

# Start up Elasticsearch in the background
/opt/elasticsearch_version/bin/elasticsearch -d

# Install and setup Logstash
cd /opt/
wget https://download.elasticsearch.org/logstash/logstash/{{ logstash_version }}.tar.gz
tar -xf logstash-{{ logstash_version }}.tar.gz
ln -s /opt/{{ logstash_version }} /opt/logstash

# Prepare a logstash.conf file for Apache logs
cat <<\EOF>/opt/logstash/logstash.conf
input {
  file {
    path => "/tmp/access_log"
    start_position => beginning
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

# Run Logstash with the new conf file in the background
/opt/logstash/bin/logstash agent -f /opt/logstash/logstash.conf -l /opt/logstash/logstash.log &

# Install and setup Kibana
cd /opt/
wget https://download.elasticsearch.org/kibana/kibana/{{ kibana_version }}.tar.gz
tar -xf kibana-{{ kibana_version }}.tar.gz

#  Copy the contents of the extracted directory to your webserver root directory
cp -R /opt/{{ kibana_version }/* /var/www/html
chown -R apache:apache /var/www/html

_eof

text_template elk_result_template

Your Elasticsearch + Logstash + Kibana setup is ready at the following IP address:

http://{{ elk_server.ipaddress_public }}/

To do:
1. Edit /var/www/kibana/config.js and set the elasticsearch parameter to 
   the fully qualified hostname of your Elasticsearch server*
2. Edit /etc/sysconfig/iptables to allow port 80 and 9200
   -A INPUT -m state --state NEW -m tcp -p tcp --dport 80 -j ACCEPT
   -A INPUT -m state --state NEW -m tcp -p tcp --dport 9200 -j ACCEPT
3. Restart iptables
   $> /etc/init.d/iptables restart
4. Start httpd
   $> /etc/init.d/httpd start
5. Go to your server address and read the Kibana dashboard for further instructions

_eof
