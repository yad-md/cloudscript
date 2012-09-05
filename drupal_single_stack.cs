cloudscript drupal_single_stack
    version              = '2012-05-20'
    result_template      = drupal_result_template  

globals
    server_password      = lib::random_password()
    console_password     = lib::random_password()
    drupal_db_password   = lib::random_password()
    drupal_db_name       = 'drupal'
    drupal_db_username   = 'drupal'
    drupal_slice_user    = 'drupal'

thread drupal_install
    tasks                = [config]
    
task config
    /key/password drupal_server_key read_or_create
        key_group        = _SERVER
        password         = server_password
    
    /key/password drupal_console_key read_or_create
        key_group        = _CONSOLE
        password         = console_password

    #
    # create drupal storage slice, bootstrap script and recipe
    #
    
    # storage slice key
    /key/token drupal_slice_key read_or_create
        username        = drupal_slice_user

    # slice
    /storage/slice drupal_slice read_or_create
        keys            = [drupal_slice_key]
    
    # slice container
    /storage/container drupal_container => [drupal_slice] read_or_create
        slice           = drupal_slice
    
    # store script as object in cloudstorage
    /storage/object drupal_install_script_object => [drupal_slice, drupal_container] read_or_create
        container_name  = 'drupal_container'
        file_name       = 'install_drupal.sh'
        slice           =  drupal_slice
        content_data    =  install_drupal_sh
        
    # associate the cloudstorage object with the drupal script
    /orchestration/script drupal_install_script => [drupal_slice, drupal_container, drupal_install_script_object] read_or_create
        data_uri        = 'cloudstorage://drupal_slice/drupal_container/install_drupal.sh'
        script_type     = _SHELL
        encoding        = _STORAGE
    
    # create the recipe and associate the script
    /orchestration/recipe drupal_install_recipe read_or_create
        scripts         = [drupal_install_script]

    # drupal node
    /server/cloud drupal_server read_or_create
        hostname        = 'drupal'
        image           = 'Linux Ubuntu Server 10.04 LTS 64-bit'
        service_type    = 'CS05'
        keys            = [drupal_server_key, drupal_console_key]
        recipes         = [drupal_install_recipe]

text_template drupal_result_template

Thank you for provisioning drupal server.

You can now finish its configuration on the following page:

http://{{ drupal_server.ipaddress_public }}/drupal

Please use following credentials for database configuration:

hostname: localhost
username: {{ drupal_db_username }}
password: {{ drupal_db_password }}
database: {{ drupal_db_name }}

You can also login to the server directly via SSH by connecting
to root@{{ drupal_server.ipaddress_public }} using the password:

{{ drupal_server_key.password }}

_eof


text_template install_drupal_sh
#!/bin/bash
#
# install drupal service
#

# check permissions
[ `whoami` = 'root' ] || {
    echo "ERROR: must have root permissions to execute the commands"
    exit 1
}

apt-get update > /dev/null
[ $? -eq 0 ] && echo "OK: update local apt cache" || {
    echo "ERROR: update local apt cache"
    exit 1
}

# install MySQL
DEBIAN_FRONTEND=noninteractive apt-get install -y mysql-server mysql-client > /dev/null
[ $? -eq 0 ] && echo "OK: install MySQL" || {
    echo "ERROR: install MySQL"
    exit 1
}

# install Apache2
DEBIAN_FRONTEND=noninteractive apt-get install -y apache2 > /dev/null
[ $? -eq 0 ] && echo "OK: install Apache2" || {
    echo "ERROR: install Apache2"
    exit 1
}

# install PHP5
apt-get install -y php5 php5-cli libapache2-mod-php5 > /dev/null
[ $? -eq 0 ] && echo "OK: install PHP5" || {
    echo "ERROR: install PHP5"
    exit 1
}
# install required PHP5 modules
apt-get install -y php5-mysql php5-gd php5-curl > /dev/null
[ $? -eq 0 ] && echo "OK: install PHP5 modules" || {
    echo "ERROR: install PHP5 modules"
    exit 1
}
# enable PHP5 module
a2enmod php5 > /dev/null

# download drupal source
cd /usr/local/src && wget -O drupal.tar.gz http://ftp.drupal.org/files/projects/drupal-7.15.tar.gz > /dev/null 2>&1
[ $? -eq 0 ] && echo "OK: download drupal source" || {
    echo "ERROR: download drupal source"
    exit 1
}
# extract drupal files
tar xzf /usr/local/src/drupal.tar.gz -C /var/www > /dev/null
[ $? -eq 0 ] && echo "OK: extract drupal files" || {
    echo "ERROR: extract drupal files"
    exit 1
}
# move drupal files
cd /var/www && mv ./drupal* ./drupal > /dev/null
[ $? -eq 0 ] && echo "OK: move drupal files" || {
    echo "ERROR: move drupal files"
    exit 1
}

# set ownership & permissions
cd /var/www/drupal &&
cp sites/default/default.settings.php sites/default/settings.php &&
chmod 666 sites/default/settings.php &&
chmod a+w sites/default
[ $? -eq 0 ] && echo "OK: set required permissions" || {
    echo "ERROR: set required permissions"
    exit 1
}

# create MySQL database & user
mysql <<EOF
create database {{ drupal_db_name }};
grant all on {{ drupal_db_name }}.* to {{ drupal_db_username }}@localhost identified by '{{ drupal_db_password }}';
EOF
[ $? -eq 0 ] && echo "OK: create drupal database and user" || {
    echo "ERROR: create drupal database and user"
    exit 1
}

# enable mod_rewrite
a2enmod rewrite > /dev/null 2>&1

# restart web server
/etc/init.d/apache2 stop  > /dev/null 2>&1
/etc/init.d/apache2 start > /dev/null 2>&1

#
# firewall setup
#

# lock down mysql access from public interface
iptables -I INPUT 1 -j DROP -p tcp --dport 3306 -s 0.0.0.0/0 -d 0.0.0.0/0 --in-interface eth0 
iptables-save > /etc/iptables.rules
cat <<EOF>/etc/init.d/firewall
#!/bin/sh

# persist iptables block on public interface access to port 3306 (mysql)
iptables-restore < /etc/iptables.rules

EOF
chmod +X /etc/init.d/firewall
update-rc.d firewall defaults &> /dev/null

echo "OK: install drupal service"

_eof