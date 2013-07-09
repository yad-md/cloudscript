cloudscript haproxy_multi_stack
    version                 = _LATEST
    result_template         = haproxy_result_template
 
globals
    server_pass             = lib::random_password()
    console_pass            = lib::random_password()
    haproxy_slice_user      = 'haproxysliceuser'
    
thread haproxy_setup
    tasks                   = [haproxy_server_setup]

task haproxy_server_setup

    #-------------------------------
    # create haproxy keys
    #-------------------------------
    
    # create haproxy server root password key
    /key/password haproxy_server_password_key read_or_create
        key_group           = _SERVER
        password			= server_pass
		
    # create haproxy server console key
    /key/password haproxy_server_console_key read_or_create
        key_group           = _CONSOLE
		password			= console_pass
        
    # create storage slice keys
    /key/token haproxy_slice_key read_or_create
        username            = haproxy_slice_user
        
    #-------------------------------
    # create slice and container
    #-------------------------------
    
    # create slice to store script in cloudstorage
    /storage/slice haproxy_slice read_or_create
        keys                = [haproxy_slice_key]
    
    # create slice container to store script in cloudstorage
    /storage/container haproxy_container => [haproxy_slice] read_or_create
        slice               = haproxy_slice

    #-------------------------------
    # create apache bootstrap 
    # script and recipe
    #-------------------------------
    	
	# place script data in cloudstorage
    /storage/object apache_bootstrap_object => [haproxy_slice, haproxy_container] read_or_create
        container_name      = 'haproxy_container'
        file_name           = 'bootstrap_apache.sh'
        slice               = haproxy_slice
        content_data        = apache_bootstrap_data
        
    # associate the cloudstorage object with the haproxy script
    /orchestration/script apache_bootstrap_script => [haproxy_slice, haproxy_container, apache_bootstrap_object] read_or_create
        data_uri            = 'cloudstorage://haproxy_slice/haproxy_container/bootstrap_apache.sh'
        script_type         = _SHELL
        encoding            = _STORAGE
    
	# create the recipe and associate the script
    /orchestration/recipe apache_bootstrap_recipe read_or_create
        scripts             = [apache_bootstrap_script]

    #-------------------------------
    # create the apache servers
    #-------------------------------

	/server/cloud apache1_server read_or_create
        hostname            = 'apache1'
        image               = 'Linux Ubuntu Server 10.04 LTS 64-bit'
        service_type        = 'CS05'
        keys                = [haproxy_server_password_key, haproxy_server_console_key]
        recipes             = [apache_bootstrap_recipe]
		
	/server/cloud apache2_server read_or_create
        hostname            = 'apache2'
        image               = 'Linux Ubuntu Server 10.04 LTS 64-bit'
        service_type        = 'CS05'
        keys                = [haproxy_server_password_key, haproxy_server_console_key]
        recipes             = [apache_bootstrap_recipe]

    #-------------------------------
    # create haproxy bootstrap 
    # script and recipe
    #-------------------------------

    # place script data in cloudstorage
    /storage/object haproxy_bootstrap_object => [haproxy_slice, haproxy_container, apache1_server, apache2_server] read_or_create
        container_name      = 'haproxy_container'
        file_name           = 'bootstrap_haproxy.sh'
        slice               = haproxy_slice
        content_data        = haproxy_bootstrap_data
        
    # associate the cloudstorage object with the haproxy script
    /orchestration/script haproxy_bootstrap_script => [haproxy_slice, haproxy_container, haproxy_bootstrap_object] read_or_create
        data_uri            = 'cloudstorage://haproxy_slice/haproxy_container/bootstrap_haproxy.sh'
        script_type         = _SHELL
        encoding            = _STORAGE

    # create the recipe and associate the script
    /orchestration/recipe haproxy_bootstrap_recipe read_or_create
        scripts             = [haproxy_bootstrap_script]

    #-------------------------------
    # create the haproxy server
    #-------------------------------
    
    /server/cloud haproxy_server read_or_create
        hostname            = 'haproxy1'
        image               = 'Linux Ubuntu Server 10.04 LTS 64-bit'
        service_type        = 'CS1'
        keys                = [haproxy_server_password_key, haproxy_server_console_key]
        recipes             = [haproxy_bootstrap_recipe]
		
#-------------------------
# Apache
#-------------------------

text_template apache_bootstrap_data
#!/bin/sh

# get latest package list
apt-get update

# install apache2
apt-get install -y apache2

echo "Server `hostname` is responding which was created at `date`" > /var/www/index.html

_eof

#-------------------------
# HAProxy
#-------------------------

text_template haproxy_bootstrap_data
#!/bin/sh

# get latest package list
apt-get update

# install haproxy
apt-get install -y haproxy

cat <<EOF>/etc/default/haproxy
ENABLED=1
EOF

# build haproxy.cfg
cat <<EOF>/etc/haproxy/haproxy.cfg
global
    log 127.0.0.1   local1 notice
    maxconn 4096
    daemon

defaults
    log global
	balance roundrobin
    mode http
    retries 3
    option redispatch
    maxconn 2000
    contimeout 5000
    clitimeout 50000
    srvtimeout 50000

frontend http

        bind 0.0.0.0:80
        mode http
        default_backend server_pool

backend server_pool

    stats enable
    stats uri /stats

EOF

# use private interfaces for free traffic between proxy and web servers
echo "    server server1 {{ apache1_server.ipaddress_private }}:80 check inter 2000 fall 3" >> /etc/haproxy/haproxy.cfg
echo "    server server2 {{ apache2_server.ipaddress_private }}:80 check inter 2000 fall 3" >> /etc/haproxy/haproxy.cfg

# start haproxy
/etc/init.d/haproxy start

_eof

text_template haproxy_result_template

Your haproxy loadbalanced site is located at:

http://{{ haproxy_server.ipaddress_public }}/

You may SSH to your servers and login with the following credentials:

haproxy: {{ haproxy_server.ipaddress_public }}
username: root
password: {{ server_pass }}

apache1: {{ apache1_server.ipaddress_public }}
username: root
password: {{ server_pass }}

apache2: {{ apache2_server.ipaddress_public }}
username: root
password: {{ server_pass }}

Your apache web servers are located at:

http://{{ apache1_server.ipaddress_public }}/
http://{{ apache2_server.ipaddress_public }}/

_eof
